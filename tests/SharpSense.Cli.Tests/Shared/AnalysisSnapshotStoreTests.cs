using AwesomeAssertions;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Cli.Shared;

namespace SharpSense.Cli.Tests.Shared;

public sealed class AnalysisSnapshotStoreTests
{
    [Fact]
    public void WhenConcurrentLanguages_ThenKeepIndependentRowsAndTerminalSnapshotsCannotRegress()
    {
        var store = new AnalysisSnapshotStore();
        var operation = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        void Send(
            long sequence,
            AnalysisNotificationKind kind,
            AnalysisSource? source = null,
            AnalysisSummary? summary = null,
            int completedItems = 1)
            => store.Notify(new(
                operation,
                sequence,
                now.AddMilliseconds(sequence),
                kind,
                AnalysisOperationKind.Full,
                Source: source,
                CompletedItems: completedItems,
                TotalItems: 4,
                Summary: summary));
        Send(1, AnalysisNotificationKind.Started);
        Send(2, AnalysisNotificationKind.SourceStarted, new(WorkspaceSourceKind.CSharp, "App.csproj"));
        Send(3, AnalysisNotificationKind.SourceStarted, new(WorkspaceSourceKind.TypeScript, "web"));
        Send(4, AnalysisNotificationKind.SourceProgress, new(WorkspaceSourceKind.CSharp, "App.csproj"), completedItems: 3);
        var loading = store.Snapshot!;
        loading.Sources.Should().SatisfyRespectively(
            source =>
            {
                source.Path.Should().Be("App.csproj");
                source.CompletedItems.Should().Be(3);
                source.TotalItems.Should().Be(4);
            },
            source =>
            {
                source.Path.Should().Be("web");
                source.CompletedItems.Should().Be(1);
                source.TotalItems.Should().Be(4);
            });
        loading.CompletedItems.Should().BeNull();
        var summary = new AnalysisSummary(2, 10, 20, 1, 2, 0, 10, 0, 0);
        Send(5, AnalysisNotificationKind.Committed, summary: summary);
        var completed = store.Snapshot;
        Send(6, AnalysisNotificationKind.SourceProgress, new(WorkspaceSourceKind.CSharp, "App.csproj"));
        store.Snapshot.Should().BeSameAs(completed);
        completed!.State.Should().Be("completed");
        completed.LastCommittedSummary.Should().Be(summary);
        loading.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void WhenNewCycles_ThenRetainCommittedSummaryAndRejectOldOperationProgress()
    {
        var store = new AnalysisSnapshotStore();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        store.Notify(new(first, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
        var summary = new AnalysisSummary(1, 3, 2, 1, 1, 0, 0, 0, 0);
        store.Notify(new(
            first,
            2,
            now,
            AnalysisNotificationKind.Committed,
            AnalysisOperationKind.Full,
            Summary: summary));
        store.Notify(new(
            second,
            1,
            now.AddSeconds(1),
            AnalysisNotificationKind.Started,
            AnalysisOperationKind.Incremental));
        var current = store.Snapshot;
        store.Notify(new(
            first,
            99,
            now.AddSeconds(2),
            AnalysisNotificationKind.EmbeddingProgress,
            AnalysisOperationKind.Full));
        store.Snapshot.Should().BeSameAs(current);
        current!.LastCommittedSummary.Should().Be(summary);
        current.Summary.Should().BeNull();
        store.Notify(new(
            second,
            2,
            now.AddSeconds(3),
            AnalysisNotificationKind.Ignored,
            AnalysisOperationKind.Incremental));
        store.Snapshot!.State.Should().Be("ignored");
        store.Snapshot.LastCommittedSummary.Should().Be(summary);
    }

    [Fact]
    public void WhenDiagnosticsAndSourceHistoryGrow_ThenBothStayBounded()
    {
        var store = new AnalysisSnapshotStore();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        store.Notify(new(id, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
        long sequence = 1;
        for (var index = 0; index < 100; index++)
        {
            store.Notify(new(
                id,
                ++sequence,
                now,
                AnalysisNotificationKind.Diagnostic,
                AnalysisOperationKind.Full,
                Message: index + new string('x', 3000)));
            store.Notify(new(
                id,
                ++sequence,
                now,
                AnalysisNotificationKind.SourceStarted,
                AnalysisOperationKind.Full,
                Source: new(
                    WorkspaceSourceKind.CSharp,
                    $"{index}.csproj")));
        }
        store.Snapshot!.Diagnostics.Count.Should().Be(20);
        store.Snapshot.Diagnostics.Should().AllSatisfy(diagnostic => diagnostic.Length.Should().Be(2000));
        store.Snapshot.Sources.Should().ContainSingle();
        store.Snapshot.Sources[0].Path.Should().Be("99.csproj");
        store.Notify(new(
            id,
            ++sequence,
            now,
            AnalysisNotificationKind.SourceProgress,
            AnalysisOperationKind.Full,
            Source: new(WorkspaceSourceKind.CSharp, "0.csproj")));
        store.Snapshot!.Sources[0].Path.Should().Be("99.csproj");
        store.Notify(new(id, ++sequence, now, AnalysisNotificationKind.Cancelled, AnalysisOperationKind.Full));
        store.Snapshot!.Sources[0].State.Should().Be("cancelled");
    }

    [Fact]
    public void WhenClockChanges_ThenDoNotPreventNewOperationsAndOldProgressIsRejected()
    {
        var store = new AnalysisSnapshotStore();
        var now = DateTimeOffset.UtcNow;
        var first = Guid.NewGuid();
        store.Notify(new(first, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
        store.Notify(new(first, 2, now, AnalysisNotificationKind.Ignored, AnalysisOperationKind.Full));

        var next = Guid.NewGuid();
        var current = store.Apply(new(
            next,
            1,
            now.AddMinutes(-1),
            AnalysisNotificationKind.Started,
            AnalysisOperationKind.Incremental));
        current.Should().NotBeNull();
        current.OperationId.Should().Be(next);
        store.Apply(new(
            first,
            3,
            now.AddSeconds(1),
            AnalysisNotificationKind.EmbeddingProgress,
            AnalysisOperationKind.Full)).Should().BeNull();
        store.Snapshot.Should().BeSameAs(current);
        store.Apply(new(next, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Incremental)).Should().BeNull();
    }

    [Fact]
    public void WhenFailure_ThenRemainsVisibleAfterDiagnosticCapacityIsReached()
    {
        var store = new AnalysisSnapshotStore();
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        store.Notify(new(id, 1, now, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
        for (var index = 0; index < 25; index++)
        {
            store.Notify(new(
                id,
                index + 2,
                now,
                AnalysisNotificationKind.Diagnostic,
                AnalysisOperationKind.Full,
                Message: $"Warning {index}"));
        }

        store.Notify(new(
            id,
            27,
            now,
            AnalysisNotificationKind.Failed,
            AnalysisOperationKind.Full,
            Message: "Database write failed"));

        store.Snapshot!.State.Should().Be("failed");
        store.Snapshot.Diagnostics.Count.Should().Be(20);
        store.Snapshot.Diagnostics[0].Should().Be("Database write failed");
        store.Snapshot.Message.Should().Be("Database write failed");
    }
}
