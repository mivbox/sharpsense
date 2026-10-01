using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Indexing.Notifications;

/// <summary>
/// Serializes synchronous worker reports and closes their lifetime before publishing a
/// terminal event. Notifications observe indexing; an observer cannot fail a graph commit.
/// </summary>
internal sealed class AnalysisOperation
{
    private readonly IAnalysisNotifier _notifier;
    private readonly AnalysisOperationKind _kind;
    private readonly Guid _id = Guid.NewGuid();
    private readonly object _gate = new();
    private readonly HashSet<AnalysisSource> _activeSources = [];
    private long _sequence;
    private bool _finished;
    private AnalysisPhase? _phase;
    private int _extractedSources;
    private int _reusedSources;
    private int _reusedEmbeddings;
    private int _generatedEmbeddings;

    public AnalysisOperation(IAnalysisNotifier notifier, AnalysisOperationKind kind)
    {
        _notifier = notifier;
        _kind = kind;
        Publish(AnalysisNotificationKind.Started);
    }

    public void Phase(AnalysisPhase phase, string? message = null)
    {
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            _phase = phase;
            Publish(AnalysisNotificationKind.PhaseChanged, message: message);
        }
    }

    public void SourceStarted(AnalysisSource source)
    {
        lock (_gate)
        {
            _activeSources.Add(source);
            Publish(AnalysisNotificationKind.SourceStarted, source);
        }
    }

    public void SourceCompleted(AnalysisSource? source, bool reused = false)
    {
        lock (_gate)
        {
            if (source is not null)
            {
                _activeSources.Remove(source);
            }

            if (reused)
            {
                _reusedSources++;
            }
            else
            {
                _extractedSources++;
            }

            Publish(reused ? AnalysisNotificationKind.SourceReused : AnalysisNotificationKind.SourceCompleted, source);
        }
    }

    public IProgress<IndexingProgress> SourceProgress(AnalysisSource? source, IProgress<IndexingProgress>? previous)
        => new InlineProgress<IndexingProgress>(progress =>
        {
            lock (_gate)
            {
                if (_finished || _phase != AnalysisPhase.Extraction ||
                    (source is not null && !_activeSources.Contains(source)))
                {
                    return;
                }

                Publish(
                    AnalysisNotificationKind.SourceProgress,
                    source,
                    progress.CurrentTask,
                    progress.CompletedItems,
                    progress.TotalItems > 0 ? progress.TotalItems : null);
                previous?.Report(progress);
            }
        });

    public IProgress<EmbeddingGenerationProgress> EmbeddingProgress(IProgress<EmbeddingGenerationProgress>? previous)
        => new InlineProgress<EmbeddingGenerationProgress>(progress =>
        {
            lock (_gate)
            {
                if (_finished || _phase != AnalysisPhase.Embeddings)
                {
                    return;
                }

                Publish(
                    AnalysisNotificationKind.EmbeddingProgress,
                    message: progress.CurrentTask,
                    completed: progress.CompletedItems,
                    total: progress.TotalItems > 0 ? progress.TotalItems : null);
                previous?.Report(progress);
            }
        });

    public void Embeddings(int reused, int generated)
    {
        lock (_gate)
        {
            _reusedEmbeddings = reused;
            _generatedEmbeddings = generated;
        }
    }

    public void Diagnostics(IReadOnlyList<string> diagnostics)
    {
        foreach (var message in diagnostics.Take(50))
        {
            Publish(AnalysisNotificationKind.Diagnostic, message: message);
        }
    }

    public void Committed(ExtractedNodes graph)
        => Finish(
            AnalysisNotificationKind.Committed,
            "Graph saved.",
            summary: new AnalysisSummary(
                graph.Projects.Count,
                graph.CodeNodes.Count,
                graph.Edges.Count,
                graph.CodeNodes.Count(node => node.NodeType == NodeType.Document),
                _extractedSources,
                _reusedSources,
                _reusedEmbeddings,
                _generatedEmbeddings,
                graph.Diagnostics.Count));

    public void Ignored() => Finish(AnalysisNotificationKind.Ignored, "No relevant workspace changes.");

    public void Failed(string message) => Finish(AnalysisNotificationKind.Failed, message);

    public void Cancelled() => Finish(AnalysisNotificationKind.Cancelled, "Analysis cancelled.");

    private void Finish(AnalysisNotificationKind kind, string? message = null, AnalysisSummary? summary = null)
    {
        lock (_gate)
        {
            Publish(kind, message: message, summary: summary);
            _finished = true;
            _activeSources.Clear();
        }
    }

    private void Publish(
        AnalysisNotificationKind kind,
        AnalysisSource? source = null,
        string? message = null,
        int? completed = null,
        int? total = null,
        AnalysisSummary? summary = null)
    {
        lock (_gate)
        {
            if (_finished)
            {
                return;
            }

            var notification = new AnalysisNotification(
                _id,
                ++_sequence,
                DateTimeOffset.UtcNow,
                kind,
                _kind,
                _phase,
                source,
                message,
                completed,
                total,
                summary);
            try
            {
                _notifier.Notify(notification);
            }
            catch (Exception)
            {
                // A presentation adapter is observational. Its failure must neither abort
                // analysis nor turn a successfully persisted graph into a reported failure.
            }
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
