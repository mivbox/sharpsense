using System.Diagnostics;
using FluentResults;
using Microsoft.Extensions.Logging;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing;

internal sealed class IndexingRunDiagnostics(
    IIndexRunStore? store,
    string kind,
    string scope,
    ILogger? logger = null) : IAsyncDisposable
{
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
    private readonly List<IndexPhaseTiming> _phases = [];
    private readonly List<IndexDiagnostic> _diagnostics = [];
    private string? _outcome;
    private long? _extractedNodes;
    private long? _reusedEmbeddings;
    private long? _generatedEmbeddings;

    public IDisposable Measure(string phase) => new PhaseTimer(phase, _phases);

    public void Embeddings(int reused, int generated)
    {
        _reusedEmbeddings = reused;
        _generatedEmbeddings = generated;
    }

    public void Extracted(ExtractedNodes nodes)
    {
        _extractedNodes = nodes.CodeNodes.Count;
        if (nodes.CodeNodes.Count == 0)
        {
            Embeddings(0, 0);
        }

        _diagnostics.AddRange(nodes.Diagnostics.Take(50).Select(message =>
            new IndexDiagnostic("extraction-diagnostic", "warning", message,
                Suggestion: "Review the source or project configuration; some declarations may be unavailable.")));
    }

    public void Succeeded() => _outcome = "succeeded";

    public void Failed(IEnumerable<IError> errors)
    {
        _outcome = "failed";
        _diagnostics.AddRange(errors.Take(50).Select(error => new IndexDiagnostic(
            "index-failed", "error", error.Message,
            error.Metadata.TryGetValue("filePath", out var filePath) ? filePath?.ToString() : null,
            "Fix the reported source or configuration error and run analyze again. The last committed graph is preserved.")));
    }

    public void Failed(Exception exception)
    {
        _outcome = "failed";
        _diagnostics.Add(new IndexDiagnostic("index-failed", "error", exception.Message,
            Suggestion: "Check the source, project configuration, and database access, then run analyze again."));
    }

    public void Cancelled()
    {
        _outcome = "cancelled";
        _diagnostics.Insert(0, new IndexDiagnostic("index-cancelled", "warning", "Indexing was cancelled.",
            Suggestion: "Run analyze again to complete the update."));
    }

    public async ValueTask DisposeAsync()
    {
        if (store is null || _outcome is null)
        {
            return;
        }

        var summary = new IndexRunSummary(
            _startedAt,
            DateTimeOffset.UtcNow,
            Stopwatch.GetElapsedTime(_startedTimestamp).TotalMilliseconds,
            _outcome,
            kind,
            scope,
            _extractedNodes,
            _reusedEmbeddings,
            _generatedEmbeddings,
            _phases,
            _diagnostics);

        // A cancelled indexing token must not discard its diagnostic record. Keep the
        // independent write bounded and never replace the actual graph operation's result.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await store.RecordAsync(summary, timeout.Token);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Could not save indexing diagnostics for {Scope}", scope);
        }
    }

    private sealed class PhaseTimer(string name, List<IndexPhaseTiming> phases) : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();

        public void Dispose() => phases.Add(new IndexPhaseTiming(name, Stopwatch.GetElapsedTime(_started).TotalMilliseconds));
    }
}
