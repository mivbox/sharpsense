using FluentResults;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Polly;
using Polly.Retry;
using Serilog;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Errors;
using System.Collections.Concurrent;
using System.IO.Abstractions;
using System.Runtime.CompilerServices;

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

    public async Task<Result<WorkspaceLoadResult>> Load(
        string targetPath,
        CancellationToken ct)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var diagnostics = new ConcurrentQueue<string>();
        var criticalDiagnostics = new ConcurrentBag<ServiceError>();
        var normalizedTargetPath = _fileSystem.Path.GetFullPath(targetPath);

        if (_activeWorkspaces.TryGetValue(normalizedTargetPath, out var activeWorkspace))
        {
            return await CreateLoadResult(activeWorkspace, diagnostics, criticalDiagnostics, ct);
        }

        var openedWorkspace = await OpenWorkspace(
            normalizedTargetPath,
            diagnostics,
            criticalDiagnostics,
            ct);

        if (openedWorkspace is null || !criticalDiagnostics.IsEmpty)
        {
            openedWorkspace?.Dispose();

            return Result.Fail(BuildFailureResult(diagnostics, criticalDiagnostics, normalizedTargetPath));
        }

        var createdSession = new WorkspaceSession(openedWorkspace, openedWorkspace.CurrentSolution, _fileSystem);
        var cachedWorkspace = _activeWorkspaces.GetOrAdd(normalizedTargetPath, createdSession);
        if (!ReferenceEquals(cachedWorkspace, createdSession))
        {
            createdSession.Dispose();
        }

        return await CreateLoadResult(cachedWorkspace, diagnostics, criticalDiagnostics, ct);
    }

    public async Task<Result<WorkspaceLoadResult>> Reload(string targetPath, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        var normalizedTargetPath = _fileSystem.Path.GetFullPath(targetPath);
        if (!_activeWorkspaces.TryGetValue(normalizedTargetPath, out var activeWorkspace))
        {
            return await Load(normalizedTargetPath, ct);
        }

        await activeWorkspace.Gate.WaitAsync(ct);
        try
        {
            return await ReloadAndReturn(
                normalizedTargetPath,
                activeWorkspace,
                new ConcurrentQueue<string>(),
                [],
                ct);
        }
        finally
        {
            activeWorkspace.Gate.Release();
        }
    }

    public async Task<Result<WorkspaceLoadResult>> UpdateDocuments(
        string targetPath,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(changedFiles);

        var diagnostics = new ConcurrentQueue<string>();
        var criticalDiagnostics = new ConcurrentBag<ServiceError>();
        var normalizedTargetPath = _fileSystem.Path.GetFullPath(targetPath);

        if (!_activeWorkspaces.TryGetValue(normalizedTargetPath, out var activeWorkspace))
        {
            return Result.Fail(
                new ServiceError(
                    ServiceErrorCode.FailedPrecondition,
                    $"Workspace '{targetPath}' must be loaded before documents can be updated."));
        }

        await activeWorkspace.Gate.WaitAsync(ct);

        try
        {
            if (changedFiles.Any(IsStructuralChange))
            {
                return await ReloadAndReturn(normalizedTargetPath, activeWorkspace, diagnostics, criticalDiagnostics, ct);
            }

            var readResults = await ReadDocumentMutations(changedFiles, normalizedTargetPath, ct);
            var updatedSolution = ApplyFileMutations(activeWorkspace, activeWorkspace.ActiveSolution, changedFiles, readResults);

            if (updatedSolution is null)
            {
                return await ReloadAndReturn(normalizedTargetPath, activeWorkspace, diagnostics, criticalDiagnostics, ct);
            }

            activeWorkspace.UpdateActiveSolution(updatedSolution);

            return CreateLoadResultUnderLock(activeWorkspace, diagnostics, criticalDiagnostics);
        }
        finally
        {
            activeWorkspace.Gate.Release();
        }
    }

    private async Task<(WorkspaceFileChange File, string Path, SourceText? Text)[]> ReadDocumentMutations(
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        string normalizedTargetPath,
        CancellationToken ct)
    {
        var mutations = changedFiles.Where(static f => f.ActionType is not WorkspaceFileChangeAction.Deleted)
            .ToList();
        var readResults = new ConcurrentBag<(WorkspaceFileChange File, string Path, SourceText? Text)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            mutations,
            parallelOptions,
            async (file, token) =>
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
                    ids = [];
                    session.DocumentIndex[result.Path] = ids;
                }
                ids.Add(documentId);
            }
        }

        return updatedSolution;
    }

    private async Task<Result<WorkspaceLoadResult>> ReloadAndReturn(
        string normalizedTargetPath,
        WorkspaceSession activeWorkspace,
        ConcurrentQueue<string> diagnostics,
        ConcurrentBag<ServiceError> criticalDiagnostics,
        CancellationToken ct)
    {
        var reloadResult = await ReplaceWorkspace(normalizedTargetPath, activeWorkspace, diagnostics, criticalDiagnostics, ct);

        if (reloadResult.IsFailed)
        {
            return Result.Fail(reloadResult.Errors);
        }

        return CreateLoadResultUnderLock(activeWorkspace, diagnostics, criticalDiagnostics);
    }

    private static bool IsStructuralChange(WorkspaceFileChange change)
        => change.GetAffectedPaths()
            .Any(CSharpIndexingPathRules.IsConfigurationPath);

    private ProjectId? FindOwningProjectId(Solution solution, string absoluteFilePath)
    {
        var fileDirectory = _fileSystem.Path.GetDirectoryName(absoluteFilePath);
        if (string.IsNullOrWhiteSpace(fileDirectory))
        {
            return null;
        }

        var bestMatch = solution.Projects
            .Where(static p => !string.IsNullOrWhiteSpace(p.FilePath))
            .Select(p => new
            {
                Project = p,
                Directory = _fileSystem.Path.GetDirectoryName(p.FilePath)!
            })
            .Where(p => absoluteFilePath.StartsWith(p.Directory, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static p => p.Directory.Length)
            .FirstOrDefault();

        return bestMatch?.Project.Id;
    }

    private Task<MSBuildWorkspace?> OpenWorkspace(
        string absoluteTargetPath,
        ConcurrentQueue<string> diagnostics,
        ConcurrentBag<ServiceError> criticalDiagnostics,
        CancellationToken ct)
    {
        // Register MSBuild before JIT-compiling the logger overloads in the async core.
        var workspace = _workspaceFactory.Create();

        return OpenWorkspaceCore(workspace, absoluteTargetPath, diagnostics, criticalDiagnostics, ct);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<MSBuildWorkspace?> OpenWorkspaceCore(
        MSBuildWorkspace workspace,
        string absoluteTargetPath,
        ConcurrentQueue<string> diagnostics,
        ConcurrentBag<ServiceError> criticalDiagnostics,
        CancellationToken ct)
    {
        MsBuildDiagnosticLog? buildLog = null;

        try
        {
            buildLog = new MsBuildDiagnosticLog();
            var workspaceDiagnostics = new ConcurrentQueue<WorkspaceDiagnostic>();
            workspace.RegisterWorkspaceFailedHandler(
                args => workspaceDiagnostics.Enqueue(args.Diagnostic));

            if (IsSolutionTargetPath(absoluteTargetPath))
            {
                await workspace.OpenSolutionAsync(absoluteTargetPath, buildLog.Logger, cancellationToken: ct);
            }
            else
            {
                await workspace.OpenProjectAsync(absoluteTargetPath, buildLog.Logger, cancellationToken: ct);
            }

            var buildDiagnostics = buildLog.Read(ct);
            foreach (var error in buildDiagnostics.Errors)
            {
                criticalDiagnostics.Add(new ServiceError(ServiceErrorCode.ThirdPartyError, error.Format("error")));
            }
            foreach (var warning in buildDiagnostics.Warnings)
            {
                diagnostics.Enqueue(warning.Format("warning"));
            }
            foreach (var diagnostic in workspaceDiagnostics)
            {
                if (!buildDiagnostics.IsConfirmedWarning(diagnostic))
                {
                    RouteWorkspaceDiagnostic(diagnostic, diagnostics, criticalDiagnostics);
                }
            }

            return workspace;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            workspace.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            workspace.Dispose();
            criticalDiagnostics.Add(
                new ServiceError(
                    ServiceErrorCode.ThirdPartyError,
                    $"MSBuild workspace failed to open '{absoluteTargetPath}': {exception.Message}"));
            _logger.Error(
                exception,
                "Workspace loader caught an exception while opening {TargetPath}; converted to ServiceError.",
                absoluteTargetPath);

            return null;
        }
        finally
        {
            // Failed or cancelled loads dispose their workspace above before removing
            // files that the external build host may still have held open.
            buildLog?.Dispose();
        }
    }

    private static void RouteWorkspaceDiagnostic(
        WorkspaceDiagnostic diagnostic,
        ConcurrentQueue<string> diagnostics,
        ConcurrentBag<ServiceError> criticalDiagnostics)
    {
        if (diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
        {
            criticalDiagnostics.Add(
                new ServiceError(ServiceErrorCode.ThirdPartyError, diagnostic.ToString()));

            return;
        }

        diagnostics.Enqueue(diagnostic.ToString());
    }

    private async Task<Result<MSBuildWorkspace>> ReplaceWorkspace(
        string absoluteTargetPath,
        WorkspaceSession activeWorkspace,
        ConcurrentQueue<string> diagnostics,
        ConcurrentBag<ServiceError> criticalDiagnostics,
        CancellationToken ct)
    {
        var replacementWorkspace = await OpenWorkspace(absoluteTargetPath, diagnostics, criticalDiagnostics, ct);
        if (replacementWorkspace is null || !criticalDiagnostics.IsEmpty)
        {
            replacementWorkspace?.Dispose();

            return Result.Fail(BuildFailureResult(diagnostics, criticalDiagnostics, absoluteTargetPath));
        }

        activeWorkspace.Replace(replacementWorkspace, replacementWorkspace.CurrentSolution, _fileSystem);

        return Result.Ok(replacementWorkspace);
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
        => string.Equals(_fileSystem.Path.GetExtension(absoluteTargetPath), ".sln", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(_fileSystem.Path.GetExtension(absoluteTargetPath), ".slnx", StringComparison.OrdinalIgnoreCase);

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static async Task<Result<WorkspaceLoadResult>> CreateLoadResult(
        WorkspaceSession workspaceSession,
        ConcurrentQueue<string> diagnostics,
        ConcurrentBag<ServiceError> criticalDiagnostics,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(workspaceSession);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(criticalDiagnostics);

        await workspaceSession.Gate.WaitAsync(ct);

        try
        {
            return CreateLoadResultUnderLock(workspaceSession, diagnostics, criticalDiagnostics);
        }
        finally
        {
            workspaceSession.Gate.Release();
        }
    }

    private static Result<WorkspaceLoadResult> CreateLoadResultUnderLock(
        WorkspaceSession workspaceSession,
        ConcurrentQueue<string> diagnostics,
        ConcurrentBag<ServiceError> criticalDiagnostics)
    {
        var errors = criticalDiagnostics.ToArray();

        return errors.Length == 0
            ? Result.Ok(new WorkspaceLoadResult(workspaceSession.ActiveSolution, diagnostics.ToArray(), errors))
            : Result.Fail(errors);
    }

    private static List<ServiceError> BuildFailureResult(
        ConcurrentQueue<string> diagnostics,
        ConcurrentBag<ServiceError> criticalDiagnostics,
        string targetPath)
    {
        var errors = criticalDiagnostics.ToArray();

        if (errors.Length == 0)
        {
            errors =
            [
                new ServiceError(
                    ServiceErrorCode.ThirdPartyError,
                    $"MSBuild workspace '{targetPath}' did not produce any projects. " +
                    $"Loader warnings: {string.Join(", ", diagnostics)}")
            ];
        }

        return errors.ToList();
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
