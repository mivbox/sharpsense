using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Polly;
using Polly.Retry;
using Serilog;
using SharpSense.Application.Indexing.Models;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class WorkspaceLoader : IWorkspaceLoader
{
    private static readonly ILogger _logger = Log.ForContext<WorkspaceLoader>();
    private static readonly ResiliencePipeline<SourceText> _documentReadPipeline = new ResiliencePipelineBuilder<SourceText>()
        .AddRetry(new RetryStrategyOptions<SourceText>
        {
            ShouldHandle = new PredicateBuilder<SourceText>()
                .Handle<IOException>()
                .Handle<UnauthorizedAccessException>(),
            MaxRetryAttempts = 5,
            Delay = TimeSpan.FromMilliseconds(25),
            BackoffType = DelayBackoffType.Exponential
        })
        .Build();

    private readonly IMsBuildWorkspaceFactory _workspaceFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ConcurrentDictionary<string, WorkspaceSession> _activeWorkspaces = new(GetPathComparer());
    private bool _disposed;

    public WorkspaceLoader(
        IMsBuildWorkspaceFactory workspaceFactory,
        IFileSystem fileSystem)
    {
        _workspaceFactory = workspaceFactory ?? throw new ArgumentNullException(nameof(workspaceFactory));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    public async Task<WorkspaceLoadResult> Load(
        string targetPath,
        RoslynWorkspaceOptions? options = null,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var diagnostics = new ConcurrentQueue<string>();
        var normalizedTargetPath = _fileSystem.Path.GetFullPath(targetPath);
        if (_activeWorkspaces.TryGetValue(normalizedTargetPath, out var activeWorkspace))
        {
            return await CreateLoadResult(activeWorkspace, diagnostics, ct);
        }

        var createdWorkspace = await OpenWorkspace(normalizedTargetPath, diagnostics, options, ct);
        var createdSession = new WorkspaceSession(createdWorkspace, createdWorkspace.CurrentSolution, options);
        var cachedWorkspace = _activeWorkspaces.GetOrAdd(normalizedTargetPath, createdSession);
        if (!ReferenceEquals(cachedWorkspace, createdSession))
        {
            createdSession.Dispose();
        }

        return await CreateLoadResult(cachedWorkspace, diagnostics, ct);
    }

    public async Task<WorkspaceLoadResult> UpdateDocuments(
        string targetPath,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(changedFiles);

        var diagnostics = new ConcurrentQueue<string>();
        var normalizedTargetPath = _fileSystem.Path.GetFullPath(targetPath);
        if (!_activeWorkspaces.TryGetValue(normalizedTargetPath, out var activeWorkspace))
        {
            throw new InvalidOperationException(
                $"Workspace '{normalizedTargetPath}' must be loaded before documents can be updated.");
        }

        await activeWorkspace.Gate.WaitAsync(ct);

        try
        {
            if (RequiresReload(changedFiles))
            {
                var reloadedSolution = await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, ct);
                return new WorkspaceLoadResult(reloadedSolution, diagnostics.ToArray());
            }

            var updatedSolution = activeWorkspace.ActiveSolution;

            foreach (var changedFile in changedFiles)
            {
                var currentPath = changedFile.GetCurrentPath();
                if (string.IsNullOrWhiteSpace(currentPath))
                {
                    continue;
                }

                var absoluteFilePath = _fileSystem.Path.GetFullPath(currentPath);
                if (!_fileSystem.File.Exists(absoluteFilePath))
                {
                    updatedSolution = await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, ct);
                    return new WorkspaceLoadResult(updatedSolution, diagnostics.ToArray());
                }

                var documentIds = FindDocumentIds(updatedSolution, absoluteFilePath);
                if (documentIds.Count == 0)
                {
                    updatedSolution = await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, ct);
                    return new WorkspaceLoadResult(updatedSolution, diagnostics.ToArray());
                }

                var documentText = await ReadDocumentText(absoluteFilePath, normalizedTargetPath, ct);
                if (documentText is null)
                {
                    updatedSolution = await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, ct);
                    return new WorkspaceLoadResult(updatedSolution, diagnostics.ToArray());
                }

                foreach (var documentId in documentIds)
                {
                    updatedSolution = updatedSolution.WithDocumentText(
                        documentId,
                        documentText,
                        PreservationMode.PreserveIdentity);
                }
            }

            activeWorkspace.UpdateActiveSolution(updatedSolution);
            return new WorkspaceLoadResult(updatedSolution, diagnostics.ToArray());
        }
        finally
        {
            activeWorkspace.Gate.Release();
        }
    }

    public async Task<WorkspaceTextUpdateResult> ChangeDocumentText(
        string targetPath,
        string documentPath,
        Func<SourceText, WorkspaceTextChange> changeText,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(changeText);

        var loadedWorkspace = await Load(
            targetPath,
            ct: ct);
        var diagnostics = new ConcurrentQueue<string>(loadedWorkspace.Diagnostics);
        var normalizedTargetPath = _fileSystem.Path.GetFullPath(targetPath);
        var absoluteDocumentPath = _fileSystem.Path.GetFullPath(documentPath);
        var activeWorkspace = _activeWorkspaces[normalizedTargetPath];

        await activeWorkspace.Gate.WaitAsync(ct);

        try
        {
            var documentIds = FindDocumentIds(
                activeWorkspace.ActiveSolution,
                absoluteDocumentPath);
            if (documentIds.Count == 0)
            {
                return new WorkspaceTextUpdateResult(
                    false,
                    diagnostics.ToArray(),
                    $"Unable to locate document '{absoluteDocumentPath}' in workspace '{normalizedTargetPath}'.");
            }

            var primaryDocument = activeWorkspace.ActiveSolution.GetDocument(documentIds[0]);
            if (primaryDocument is null)
            {
                return new WorkspaceTextUpdateResult(
                    false,
                    diagnostics.ToArray(),
                    $"Unable to load document '{absoluteDocumentPath}' from workspace '{normalizedTargetPath}'.");
            }

            var sourceText = await primaryDocument.GetTextAsync(ct);
            var requestedChange = changeText(sourceText);
            ArgumentNullException.ThrowIfNull(requestedChange);

            if (!requestedChange.Success)
            {
                return new WorkspaceTextUpdateResult(
                    false,
                    diagnostics.ToArray(),
                    requestedChange.ErrorMessage);
            }

            var updatedText = requestedChange.UpdatedText
                              ?? throw new InvalidOperationException("Successful workspace text changes must supply updated text.");
            if (!activeWorkspace.Workspace.CanApplyChange(ApplyChangesKind.ChangeDocument))
            {
                return new WorkspaceTextUpdateResult(
                    false,
                    diagnostics.ToArray(),
                    $"Workspace '{normalizedTargetPath}' does not support document text changes.");
            }

            var updatedSolution = activeWorkspace.ActiveSolution;

            foreach (var documentId in documentIds)
            {
                updatedSolution = updatedSolution.WithDocumentText(
                    documentId,
                    updatedText,
                    PreservationMode.PreserveIdentity);
            }

            if (!activeWorkspace.Workspace.TryApplyChanges(updatedSolution))
            {
                return new WorkspaceTextUpdateResult(
                    false,
                    diagnostics.ToArray(),
                    $"Roslyn could not apply changes to document '{absoluteDocumentPath}'.");
            }

            activeWorkspace.UpdateActiveSolution(activeWorkspace.Workspace.CurrentSolution);

            return new WorkspaceTextUpdateResult(
                true,
                diagnostics.ToArray(),
                string.Empty);
        }
        finally
        {
            activeWorkspace.Gate.Release();
        }
    }

    private async Task<MSBuildWorkspace> OpenWorkspace(
        string absoluteTargetPath,
        ConcurrentQueue<string> diagnostics,
        RoslynWorkspaceOptions? options,
        CancellationToken ct)
    {
        var workspace = _workspaceFactory.Create(options);

        try
        {
            workspace.RegisterWorkspaceFailedHandler(
                args => diagnostics.Enqueue(args.Diagnostic.ToString()),
                null);

            if (IsSolutionTargetPath(absoluteTargetPath))
            {
                await workspace.OpenSolutionAsync(absoluteTargetPath, cancellationToken: ct);
            }
            else
            {
                await workspace.OpenProjectAsync(absoluteTargetPath, cancellationToken: ct);
            }

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private async Task<Solution> ReplaceWorkspace(
        string absoluteTargetPath,
        WorkspaceSession activeWorkspace,
        ConcurrentQueue<string> diagnostics,
        CancellationToken ct)
    {
        var replacementWorkspace = await OpenWorkspace(
            absoluteTargetPath,
            diagnostics,
            activeWorkspace.Options,
            ct);

        activeWorkspace.Replace(replacementWorkspace, replacementWorkspace.CurrentSolution);
        return activeWorkspace.ActiveSolution;
    }

    private IReadOnlyList<DocumentId> FindDocumentIds(
        Solution solution,
        string absoluteFilePath)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);

        var pathComparer = GetPathComparer();

        return
        [
            .. solution.Projects
                .SelectMany(static project => project.Documents)
                .Where(document => !string.IsNullOrWhiteSpace(document.FilePath) &&
                                   pathComparer.Equals(_fileSystem.Path.GetFullPath(document.FilePath), absoluteFilePath))
                .Select(static document => document.Id)
        ];
    }

    private static bool RequiresReload(IReadOnlyList<WorkspaceFileChange> changedFiles)
        => changedFiles.Any(changedFile => changedFile.ActionType is not WorkspaceFileChangeAction.Modified);

    private async Task<SourceText?> ReadDocumentText(
        string absoluteFilePath,
        string normalizedTargetPath,
        CancellationToken ct)
    {
        try
        {
            return await _documentReadPipeline.ExecuteAsync(
                async cancellationToken =>
                {
                    var documentContents = await _fileSystem.File.ReadAllTextAsync(absoluteFilePath, cancellationToken);
                    return SourceText.From(documentContents);
                },
                ct);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(
                exception,
                "Workspace loader fell back to reloading target {TargetPath} after incremental document read failed for {DocumentPath}.",
                normalizedTargetPath,
                absoluteFilePath);

            return null;
        }
    }

    private bool IsSolutionTargetPath(string absoluteTargetPath)
        => string.Equals(_fileSystem.Path.GetExtension(absoluteTargetPath), ".sln", StringComparison.OrdinalIgnoreCase);

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static async Task<WorkspaceLoadResult> CreateLoadResult(
        WorkspaceSession workspaceSession,
        ConcurrentQueue<string> diagnostics,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workspaceSession);
        ArgumentNullException.ThrowIfNull(diagnostics);

        await workspaceSession.Gate.WaitAsync(ct);

        try
        {
            return new WorkspaceLoadResult(workspaceSession.ActiveSolution, diagnostics.ToArray());
        }
        finally
        {
            workspaceSession.Gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var targetPath in _activeWorkspaces.Keys)
        {
            if (_activeWorkspaces.TryRemove(targetPath, out var workspaceSession))
            {
                workspaceSession.Dispose();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WorkspaceLoader));
        }
    }
}

internal sealed class WorkspaceSession(
    MSBuildWorkspace workspace,
    Solution activeSolution,
    RoslynWorkspaceOptions? options) : IDisposable
{
    public MSBuildWorkspace Workspace { get; private set; } = workspace ?? throw new ArgumentNullException(nameof(workspace));

    public Solution ActiveSolution { get; private set; } = activeSolution ?? throw new ArgumentNullException(nameof(activeSolution));

    public RoslynWorkspaceOptions? Options { get; } = options;

    public SemaphoreSlim Gate { get; } = new(1, 1);

    public void UpdateActiveSolution(Solution activeSolution)
        => ActiveSolution = activeSolution ?? throw new ArgumentNullException(nameof(activeSolution));

    public void Replace(
        MSBuildWorkspace workspace,
        Solution activeSolution)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(activeSolution);

        var previousWorkspace = Workspace;
        Workspace = workspace;
        ActiveSolution = activeSolution;
        previousWorkspace.Dispose();
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Gate.Dispose();
    }
}
