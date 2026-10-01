using FluentResults;
using Microsoft.Extensions.Logging;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Application.Shared.Models;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace SharpSense.Application.Indexing;

/// <summary>
/// Owns one indexing session's successful source contributions. Language workers only
/// extract; the caller merges, embeds and publishes one complete graph before committing this cache.
/// Watch batches are supplied serially by the session; cancelling a session ends its cache lifetime.
/// </summary>
internal sealed class WorkspaceExtractionCoordinator(
    IEnumerable<ILanguageExtractor> extractors,
    IIndexingWorkspacePaths paths,
    IWorkspaceChangeFilter changeFilter,
    ILogger<WorkspaceExtractionCoordinator> logger) : IDisposable
{
    private readonly SemaphoreSlim _sessionGate = new(1, 1);
    private CommittedContributions? _committed;

    internal async Task<IDisposable> Acquire(CancellationToken ct)
    {
        await _sessionGate.WaitAsync(ct);

        return new SessionLease(_sessionGate);
    }

    internal async Task<Result<WorkspaceExtractionBatch>> Extract(
        string? workspaceId,
        IReadOnlyList<WorkspaceSource> sources,
        ExtractionContext context,
        CancellationToken ct,
        AnalysisOperation? analysis = null)
    {
        var key = JsonSerializer.Serialize(new
        {
            workspaceId,
            paths.RootPath,
            sources
        });
        var previous = _committed?.Key == key && IsDocumentationOnly(context.ChangedFiles)
            ? _committed
            : null;

        // A failed extraction, merge, embedding or commit must not make a later docs edit
        // reuse contributions from before an unsuccessful code/configuration change.
        _committed = null;
        var progress = context.Progress is null ? null : new SerialProgress(context.Progress);
        var plan = WorkspaceExtractionPlan.Create(
            sources,
            extractors,
            paths,
            context with
            {
                Progress = progress
            });
        var contributions = new ExtractedNodes[plan.Count];
        var results = new Result<ExtractedNodes>?[plan.Count];
        ExceptionDispatchInfo? workerFailure = null;
        using var workers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var groups = plan
            .Select((step, index) => (Step: step, Index: index))
            .GroupBy(item => item.Step.Source.Kind);

        try
        {
            // Independent languages overlap even when discovery/parsing runs synchronously.
            // Each language stays serial: Roslyn and TypeScript own mutable session caches.
            // ForEachAsync joins every worker before returning, including cancellation/failure.
            await Parallel.ForEachAsync(
                groups,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 3,
                    CancellationToken = workers.Token
                },
                async (group, token) =>
            {
                try
                {
                    foreach (var (step, index) in group)
                    {
                        token.ThrowIfCancellationRequested();
                        var source = new AnalysisSource(step.Source.Kind, step.Source.Path);
                        if (previous is not null && CanReuse(
                            step,
                            previous.Contributions[index],
                            context.ChangedFiles!))
                        {
                            contributions[index] = previous.Contributions[index];
                            progress?.Report(new IndexingProgress(
                                $"Reusing {step.Source.Kind} source '{step.Source.Path}'...",
                                0,
                                1));
                            analysis?.SourceCompleted(source, reused: true);
                            continue;
                        }

                        analysis?.SourceStarted(source);
                        var result = await step.Extractor.Extract(
                            step.Context with
                            {
                                Progress = analysis?.SourceProgress(
                                    source,
                                    step.Context.Progress) ?? step.Context.Progress
                            },
                            token);
                        results[index] = result;
                        if (result.IsFailed)
                        {
                            await workers.CancelAsync();

                            return;
                        }

                        contributions[index] = Normalize(WorkspaceExtractionPlan.SelectEmittedProjects(
                            step,
                            result.Value,
                            paths));
                        analysis?.SourceCompleted(source);
                    }
                }
                catch (Exception exception)
                {
                    if (exception is not OperationCanceledException || !token.IsCancellationRequested)
                    {
                        Interlocked.CompareExchange(ref workerFailure, ExceptionDispatchInfo.Capture(exception), null);
                    }
                    await workers.CancelAsync();
                    throw;
                }
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested &&
            (workerFailure is not null || results.Any(result => result?.IsFailed == true)))
        {
            // Another language failed and cancelled its siblings. Preserve its actual errors.
        }

        ct.ThrowIfCancellationRequested();
        workerFailure?.Throw();
        var errors = results
            .Where(result => result?.IsFailed == true)
            .SelectMany(result => result!.Errors)
            .ToArray();
        if (errors.Length > 0)
        {
            return Result.Fail(errors);
        }

        // Keep shared change-filter dictionaries outside workers. Unfiltered semantic inputs
        // from referenced C# projects must still be tracked even when their declarations aren't selected.
        for (var index = 0; index < plan.Count; index++)
        {
            logger.LogDebug(
                new EventId(1101, "WorkspaceSourceContribution"),
                "Workspace source {SourceKind} '{SourcePath}': {Action}.",
                plan[index].Source.Kind,
                plan[index].Source.Path,
                results[index] is null ? "reused" : "extracted");
            if (results[index] is { IsSuccess: true } result)
            {
                changeFilter.TrackSource(plan[index].Source, result.Value);
            }
        }

        var merged = WorkspaceGraphMerger.Merge(contributions);

        return merged.IsFailed
            ? Result.Fail(merged.Errors)
            : Result.Ok(new WorkspaceExtractionBatch(key, contributions, merged.Value));
    }

    internal void Commit(WorkspaceExtractionBatch batch)
    {
        _committed = new CommittedContributions(batch.Key, batch.Contributions);
    }

    private bool CanReuse(
        WorkspaceExtractionStep step,
        ExtractedNodes contribution,
        IReadOnlyList<WorkspaceFileChange> changes)
    {
        if (step.Source.Kind == WorkspaceSourceKind.Markdown || !contribution.CanReuseForDocumentationChanges)
        {
            return false;
        }

        // A new/renamed Markdown file could match an AdditionalFiles glob that had no
        // previous matches. Reevaluate C# rather than guessing MSBuild's item membership.
        if (step.Source.Kind == WorkspaceSourceKind.CSharp &&
            changes
                .Any(change => change.ActionType is WorkspaceFileChangeAction.Added or WorkspaceFileChangeAction.Renamed))
        {
            return false;
        }

        var inputs = (contribution.InputPaths ?? [])
            .Select(path => paths.TryToRepositoryRelativePath(path, out var relativePath) ? relativePath : null)
            .OfType<string>()
            // Declared MSBuild paths can differ in casing from native watcher paths on
            // case-insensitive macOS volumes. Extra invalidation is safe on other volumes.
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return changes
            .SelectMany(change => change.GetAffectedPaths())
            .All(path => paths.TryToRepositoryRelativePath(
                path,
                out var relativePath) && !inputs.Contains(relativePath));
    }

    private static bool IsDocumentationOnly(IReadOnlyList<WorkspaceFileChange>? changes)
        => changes is { Count: > 0 } && changes.All(change =>
            change.ActionType is WorkspaceFileChangeAction.Added or WorkspaceFileChangeAction.Modified
                or WorkspaceFileChangeAction.Deleted or WorkspaceFileChangeAction.Renamed &&
            change.GetAffectedPaths()
                .Any() &&
            change.GetAffectedPaths()
                .All(path =>
                Path.GetExtension(path)
                    .ToLowerInvariant() is ".md" or ".markdown" or ".mdown" or ".mkd"));

    private ExtractedNodes Normalize(ExtractedNodes contribution)
        => contribution with
        {
            Projects =
            [
                .. contribution.Projects
                    .Select(project => project with
                    {
                        RelativeFilePath = paths.ToRepositoryRelativePath(project.RelativeFilePath)
                    })
            ],
            CodeNodes =
            [
                .. contribution.CodeNodes
                    .Select(node => node with
                    {
                        RelativeFilePath = paths.ToRepositoryRelativePath(node.RelativeFilePath)
                    })
            ],
            Edges = [.. contribution.Edges],
            Diagnostics = [.. contribution.Diagnostics],
            InputPaths = contribution.InputPaths is null ? null : [.. contribution.InputPaths]
        };

    public void Dispose() => _sessionGate.Dispose();

    private sealed record CommittedContributions(string Key, IReadOnlyList<ExtractedNodes> Contributions);

    private sealed class SessionLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }

    private sealed class SerialProgress(IProgress<IndexingProgress> progress) : IProgress<IndexingProgress>
    {
        private readonly object _gate = new();

        public void Report(IndexingProgress value)
        {
            lock (_gate)
            {
                progress.Report(value);
            }
        }
    }
}
