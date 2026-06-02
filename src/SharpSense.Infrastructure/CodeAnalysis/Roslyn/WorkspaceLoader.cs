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
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var diagnostics = new ConcurrentQueue<string>();
        var normalizedTargetPath = _fileSystem.Path.GetFullPath(targetPath);

        if (_activeWorkspaces.TryGetValue(normalizedTargetPath, out var activeWorkspace))
        {
            return await CreateLoadResult(activeWorkspace, diagnostics, ct);
        }

        var createdWorkspace = await OpenWorkspace(normalizedTargetPath, diagnostics, ct);
        var createdSession = new WorkspaceSession(createdWorkspace, createdWorkspace.CurrentSolution, _fileSystem);

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
            throw new InvalidOperationException($"Workspace '{normalizedTargetPath}' must be loaded before documents can be updated.");
        }

        await activeWorkspace.Gate.WaitAsync(ct);

        try
        {
            if (changedFiles.Any(IsStructuralChange))
            {
                return await ReloadAndReturn(normalizedTargetPath, activeWorkspace, diagnostics, ct);
            }

            var readResults = await ReadDocumentMutationsAsync(changedFiles, normalizedTargetPath, ct);
            var updatedSolution = ApplyFileMutations(activeWorkspace, activeWorkspace.ActiveSolution, changedFiles, readResults);

            if (updatedSolution is null)
            {
                return await ReloadAndReturn(normalizedTargetPath, activeWorkspace, diagnostics, ct);
            }

            activeWorkspace.UpdateActiveSolution(updatedSolution);
            return new WorkspaceLoadResult(updatedSolution, diagnostics.ToArray());
        }
        finally
        {
            activeWorkspace.Gate.Release();
        }
    }

    private async Task<(WorkspaceFileChange File, string Path, SourceText? Text)[]> ReadDocumentMutationsAsync(
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        string normalizedTargetPath,
        CancellationToken ct)
    {
        var mutations = changedFiles.Where(static f => f.ActionType is not WorkspaceFileChangeAction.Deleted).ToList();
        var readResults = new ConcurrentBag<(WorkspaceFileChange File, string Path, SourceText? Text)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(mutations, parallelOptions, async (file, token) =>
        {
            var path = _fileSystem.Path.GetFullPath(file.GetCurrentPath()!);
            var text = await ReadDocumentText(path, normalizedTargetPath, token);
            readResults.Add((File: file, Path: path, Text: text));
        });

        return readResults.ToArray();
    }

    private Solution? ApplyFileMutations(
        WorkspaceSession session,
        Solution currentSolution,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        (WorkspaceFileChange File, string Path, SourceText? Text)[] readResults)
    {
        var updatedSolution = currentSolution;

        foreach (var change in changedFiles.Where(static f => f.ActionType is WorkspaceFileChangeAction.Deleted))
        {
            if (string.IsNullOrWhiteSpace(change.OldPath))
            {
                return null;
            }

            var pathToRemove = _fileSystem.Path.GetFullPath(change.OldPath);
            if (!session.DocumentIndex.Remove(pathToRemove, out var documentIds) || documentIds.Count == 0)
            {
                return null;
            }

            updatedSolution = documentIds.Aggregate(updatedSolution, (current, id) => current.RemoveDocument(id));
        }

        foreach (var result in readResults)
        {
            if (result.Text is null)
            {
                return null;
            }

            if (result.File.ActionType is WorkspaceFileChangeAction.Modified)
            {
                if (!session.DocumentIndex.TryGetValue(result.Path, out var documentIds) || documentIds.Count == 0)
                {
                    return null;
                }

                foreach (var id in documentIds)
                {
                    updatedSolution = updatedSolution.WithDocumentText(id, result.Text, PreservationMode.PreserveIdentity);
                }
            }
            else if (result.File.ActionType is WorkspaceFileChangeAction.Added)
            {
                var projectId = FindOwningProjectId(updatedSolution, result.Path);
                if (projectId is null)
                {
                    return null;
                }

                var documentId = DocumentId.CreateNewId(projectId);
                updatedSolution = updatedSolution.AddDocument(
                    documentId,
                    _fileSystem.Path.GetFileName(result.Path),
                    result.Text,
                    filePath: result.Path);

                if (!session.DocumentIndex.TryGetValue(result.Path, out var ids))
                {
                    ids = new List<DocumentId>();
                    session.DocumentIndex[result.Path] = ids;
                }
                ids.Add(documentId);
            }
        }

        return updatedSolution;
    }

    private async Task<WorkspaceLoadResult> ReloadAndReturn(
        string normalizedTargetPath,
        WorkspaceSession activeWorkspace,
        ConcurrentQueue<string> diagnostics,
        CancellationToken ct)
    {
        var reloadedSolution = await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, ct);
        return new WorkspaceLoadResult(reloadedSolution, diagnostics.ToArray());
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

        var loadedWorkspace = await Load(targetPath, ct: ct);
        var diagnostics = new ConcurrentQueue<string>(loadedWorkspace.Diagnostics);
        var normalizedTargetPath = _fileSystem.Path.GetFullPath(targetPath);
        var absoluteDocumentPath = _fileSystem.Path.GetFullPath(documentPath);
        var activeWorkspace = _activeWorkspaces[normalizedTargetPath];

        await activeWorkspace.Gate.WaitAsync(ct);

        try
        {
            if (!activeWorkspace.DocumentIndex.TryGetValue(absoluteDocumentPath, out var documentIds) || documentIds.Count == 0)
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
                return new WorkspaceTextUpdateResult(false, diagnostics.ToArray(), requestedChange.ErrorMessage);
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

            return new WorkspaceTextUpdateResult(true, diagnostics.ToArray(), string.Empty);
        }
        finally
        {
            activeWorkspace.Gate.Release();
        }
    }

    private bool IsStructuralChange(WorkspaceFileChange change)
    {
        var path = change.GetCurrentPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = _fileSystem.Path.GetExtension(path);
        return string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase);
    }

    private ProjectId? FindOwningProjectId(Solution solution, string absoluteFilePath)
    {
        var fileDirectory = _fileSystem.Path.GetDirectoryName(absoluteFilePath);
        if (string.IsNullOrWhiteSpace(fileDirectory))
        {
            return null;
        }

        var bestMatch = solution.Projects
            .Where(static p => !string.IsNullOrWhiteSpace(p.FilePath))
            .Select(p => new { Project = p, Directory = _fileSystem.Path.GetDirectoryName(p.FilePath)! })
            .Where(p => absoluteFilePath.StartsWith(p.Directory, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static p => p.Directory.Length)
            .FirstOrDefault();

        return bestMatch?.Project.Id;
    }

    private async Task<MSBuildWorkspace> OpenWorkspace(
        string absoluteTargetPath,
        ConcurrentQueue<string> diagnostics,
        CancellationToken ct)
    {
        var workspace = _workspaceFactory.Create();

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
        var replacementWorkspace = await OpenWorkspace(absoluteTargetPath, diagnostics, ct);
        activeWorkspace.Replace(replacementWorkspace, replacementWorkspace.CurrentSolution, _fileSystem);
        return activeWorkspace.ActiveSolution;
    }

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

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, nameof(WorkspaceLoader));
}
