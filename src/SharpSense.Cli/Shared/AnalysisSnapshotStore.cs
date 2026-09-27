using SharpSense.Application.Indexing.Notifications;

namespace SharpSense.Cli.Shared;

/// <summary>
/// Reduces serialized operation notifications into bounded, immutable presentation state.
/// A language runs its selected sources serially, so only its current/last source is retained.
/// </summary>
internal sealed class AnalysisSnapshotStore : IAnalysisNotifier
{
    private readonly object _gate = new();
    private AnalysisSnapshot? _snapshot;

    public AnalysisSnapshot? Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Notify(AnalysisNotification notification) => Apply(notification);

    /// <returns>The updated snapshot, or null for an obsolete notification.</returns>
    public AnalysisSnapshot? Apply(AnalysisNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        lock (_gate)
        {
            if (notification.Kind == AnalysisNotificationKind.Started)
            {
                // Operation starts are delivered synchronously by the serialized runner.
                // Wall-clock timestamps are for display, never for deciding operation order.
                if (_snapshot?.OperationId == notification.OperationId)
                {
                    return null;
                }

                _snapshot = new AnalysisSnapshot(
                    notification.OperationId,
                    notification.Sequence,
                    notification.OperationKind,
                    "running",
                    notification.Phase,
                    notification.Timestamp,
                    notification.Timestamp,
                    null,
                    Limit(notification.Message),
                    null,
                    null,
                    [],
                    [],
                    null,
                    _snapshot?.LastCommittedSummary);

                return _snapshot;
            }

            if (_snapshot is not { State: "running" } current ||
                current.OperationId != notification.OperationId || notification.Sequence <= current.Sequence)
            {
                return null;
            }

            var state = notification.Kind switch
            {
                AnalysisNotificationKind.Committed => "completed",
                AnalysisNotificationKind.Ignored => "ignored",
                AnalysisNotificationKind.Failed => "failed",
                AnalysisNotificationKind.Cancelled => "cancelled",
                _ => current.State
            };
            var sources = current.Sources;
            if (notification.Source is { } source)
            {
                var existing = sources.FirstOrDefault(row => row.Kind == source.Kind);
                if (notification.Kind != AnalysisNotificationKind.SourceProgress ||
                    existing is null || existing.Path == source.Path && existing.State == "running")
                {
                    var sourceState = notification.Kind switch
                    {
                        AnalysisNotificationKind.SourceCompleted => "completed",
                        AnalysisNotificationKind.SourceReused => "reused",
                        _ => "running"
                    };
                    sources = [.. sources.Where(row => row.Kind != source.Kind)
                        .Append(new AnalysisSourceStatus(
                            source.Kind,
                            source.Path,
                            sourceState,
                            notification.CompletedItems,
                            notification.TotalItems,
                            Limit(notification.Message)))
                        .OrderBy(row => row.Kind)];
                }
            }

            if (state is "failed" or "cancelled")
            {
                sources = [.. sources.Select(row => row.State == "running"
                    ? row with
                {
                    State = state
                }
                    : row)];
            }

            var diagnostics = current.Diagnostics;
            if (notification.Kind is AnalysisNotificationKind.Diagnostic or AnalysisNotificationKind.Failed &&
                !string.IsNullOrWhiteSpace(notification.Message))
            {
                var message = Limit(notification.Message)!;
                diagnostics = notification.Kind == AnalysisNotificationKind.Failed
                    ? [.. diagnostics.Prepend(message)
                        .Distinct(StringComparer.Ordinal)
                        .Take(20)]
                    : [.. diagnostics.Append(message)
                        .Distinct(StringComparer.Ordinal)
                        .Take(20)];
            }

            // Source counters belong to individual rows, not a fictitious overall percentage.
            var phaseChanged = notification.Kind == AnalysisNotificationKind.PhaseChanged;
            var embeddingProgress = notification.Kind == AnalysisNotificationKind.EmbeddingProgress;
            _snapshot = current with
            {
                Sequence = notification.Sequence,
                State = state,
                Phase = notification.Phase ?? current.Phase,
                UpdatedAt = notification.Timestamp,
                CompletedAt = state == "running" ? null : notification.Timestamp,
                Message = Limit(notification.Message) ?? current.Message,
                CompletedItems = embeddingProgress ? notification.CompletedItems : phaseChanged ? null : current.CompletedItems,
                TotalItems = embeddingProgress ? notification.TotalItems : phaseChanged ? null : current.TotalItems,
                Sources = sources,
                Diagnostics = diagnostics,
                Summary = notification.Summary ?? current.Summary,
                LastCommittedSummary = notification.Kind == AnalysisNotificationKind.Committed
                    ? notification.Summary
                    : current.LastCommittedSummary
            };

            return _snapshot;
        }
    }

    private static string? Limit(string? text) => text is { Length: > 2000 }
        ? text[..2000]
        : text;
}
