using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Notifications;

namespace SharpSense.Application.Tests.Features.Indexing;

public sealed class AnalysisSnapshotStoreTests
{
    [Fact]
    public void ConcurrentLanguagesKeepIndependentRowsAndTerminalSnapshotsCannotRegress()
    {
        var store = new AnalysisSnapshotStore();
        var operation = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        void Send(long sequence, AnalysisNotificationKind kind, AnalysisSource? source = null, AnalysisSummary? summary = null)
            => store.Notify(new(operation, sequence, now.AddMilliseconds(sequence), kind, AnalysisOperationKind.Full,
                Source: source, CompletedItems: 1, TotalItems: 4, Summary: summary));
        Send(1, AnalysisNotificationKind.Started);
        Send(2, AnalysisNotificationKind.SourceStarted, new(WorkspaceSourceKind.CSharp, "App.csproj"));
        Send(3, AnalysisNotificationKind.SourceStarted, new(WorkspaceSourceKind.TypeScript, "web"));
        Send(4, AnalysisNotificationKind.SourceProgress, new(WorkspaceSourceKind.CSharp, "App.csproj"));
        var loading = store.Snapshot!;
        Assert.Equal(2, loading.Sources.Count);
        Assert.Null(loading.CompletedItems);
        var summary = new AnalysisSummary(2, 10, 20, 1, 2, 0, 10, 0, 0);
        Send(5, AnalysisNotificationKind.Committed, summary: summary);
        var completed = store.Snapshot;
        Send(6, AnalysisNotificationKind.SourceProgress, new(WorkspaceSourceKind.CSharp, "App.csproj"));
        Assert.Same(completed, store.Snapshot);
        Assert.Equal("completed", completed!.State);
        Assert.Equal(summary, completed.LastCommittedSummary);
        Assert.Null(loading.CompletedAt);
    }

    [Fact]
    public void NewCyclesRetainCommittedSummaryAndRejectOldOperationProgress()
    {
        var store = new AnalysisSnapshotStore();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        store.Notify(new(first, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
        var summary = new AnalysisSummary(1, 3, 2, 1, 1, 0, 0, 0, 0);
        store.Notify(new(first, 2, now, AnalysisNotificationKind.Committed, AnalysisOperationKind.Full, Summary: summary));
        store.Notify(new(second, 1, now.AddSeconds(1), AnalysisNotificationKind.Started, AnalysisOperationKind.Incremental));
        var current = store.Snapshot;
        store.Notify(new(first, 99, now.AddSeconds(2), AnalysisNotificationKind.EmbeddingProgress, AnalysisOperationKind.Full));
        Assert.Same(current, store.Snapshot);
        Assert.Equal(summary, current!.LastCommittedSummary);
        Assert.Null(current.Summary);
        store.Notify(new(second, 2, now.AddSeconds(3), AnalysisNotificationKind.Ignored, AnalysisOperationKind.Incremental));
        Assert.Equal("ignored", store.Snapshot!.State);
        Assert.Equal(summary, store.Snapshot.LastCommittedSummary);
    }

    [Fact]
    public void DiagnosticsAndSourceHistoryStayBounded()
    {
        var store = new AnalysisSnapshotStore();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        store.Notify(new(id, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
        long sequence = 1;
        for (var index = 0; index < 100; index++)
        {
            store.Notify(new(id, ++sequence, now, AnalysisNotificationKind.Diagnostic, AnalysisOperationKind.Full,
                Message: index + new string('x', 3000)));
            store.Notify(new(id, ++sequence, now, AnalysisNotificationKind.SourceStarted, AnalysisOperationKind.Full,
                Source: new(WorkspaceSourceKind.CSharp, $"{index}.csproj")));
        }
        Assert.Equal(20, store.Snapshot!.Diagnostics.Count);
        Assert.All(store.Snapshot.Diagnostics, diagnostic => Assert.Equal(2000, diagnostic.Length));
        Assert.Single(store.Snapshot.Sources);
        var path = store.Snapshot.Sources[0].Path;
        store.Notify(new(id, ++sequence, now, AnalysisNotificationKind.SourceProgress, AnalysisOperationKind.Full,
            Source: new(WorkspaceSourceKind.CSharp, "0.csproj")));
        Assert.Equal(path, store.Snapshot!.Sources[0].Path);
        store.Notify(new(id, ++sequence, now, AnalysisNotificationKind.Cancelled, AnalysisOperationKind.Full));
        Assert.Equal("cancelled", store.Snapshot!.Sources[0].State);
    }

    [Fact]
    public void ClockChangesDoNotPreventNewOperationsAndOldProgressIsRejected()
    {
        var store = new AnalysisSnapshotStore();
        var now = DateTimeOffset.UtcNow;
        var first = Guid.NewGuid();
        store.Notify(new(first, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
        store.Notify(new(first, 2, now, AnalysisNotificationKind.Ignored, AnalysisOperationKind.Full));

        var next = Guid.NewGuid();
        var current = store.Apply(new(next, 1, now.AddMinutes(-1),
            AnalysisNotificationKind.Started, AnalysisOperationKind.Incremental));
        Assert.NotNull(current);
        Assert.Equal(next, current.OperationId);
        Assert.Null(store.Apply(new(first, 3, now.AddSeconds(1),
            AnalysisNotificationKind.EmbeddingProgress, AnalysisOperationKind.Full)));
        Assert.Same(current, store.Snapshot);
        Assert.Null(store.Apply(new(next, 1, now,
            AnalysisNotificationKind.Started, AnalysisOperationKind.Incremental)));
    }

    [Fact]
    public void FailureRemainsVisibleAfterDiagnosticCapacityIsReached()
    {
        var store = new AnalysisSnapshotStore();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        store.Notify(new(id, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
        for (var index = 0; index < 25; index++)
        {
            store.Notify(new(id, index + 2, now, AnalysisNotificationKind.Diagnostic,
                AnalysisOperationKind.Full, Message: $"Warning {index}"));
        }

        store.Notify(new(id, 27, now, AnalysisNotificationKind.Failed,
            AnalysisOperationKind.Full, Message: "Database write failed"));

        Assert.Equal("failed", store.Snapshot!.State);
        Assert.Equal(20, store.Snapshot.Diagnostics.Count);
        Assert.Equal("Database write failed", store.Snapshot.Diagnostics[0]);
        Assert.Equal("Database write failed", store.Snapshot.Message);
    }
}
