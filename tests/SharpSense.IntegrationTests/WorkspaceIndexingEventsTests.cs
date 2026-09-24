using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceIndexingEventsTests
{
    [Fact]
    public async Task SubscribersStartWithCurrentStateAndSlowReadersReceiveOnlyLatestState()
    {
        var selection = Selection();
        Action<WorkspaceIndexingUpdate>? publish = null;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = Create(async (_, _, update, ct) =>
        {
            publish = update;
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        using var first = coordinator.Subscribe(selection.Definition.Id);
        Assert.True(first.Reader.TryRead(out var initial));
        Assert.Equal("idle", initial.State);
        Assert.NotNull(initial.StreamId);
        using var otherWorkspace = coordinator.Subscribe(Guid.NewGuid());
        Assert.True(otherWorkspace.Reader.TryRead(out _));

        coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        for (var index = 0; index < 1000; index++)
        {
            publish!(new WorkspaceIndexingUpdate("indexing", $"Progress {index}"));
        }
        publish!(new WorkspaceIndexingUpdate("watching", "Saved.", IndexCommitted: true));
        Assert.True(first.Reader.TryRead(out var latest));
        Assert.Equal(coordinator.GetStatus(selection.Definition.Id), latest);
        Assert.Equal(1, latest.Revision);
        Assert.True(latest.Sequence > 1000);
        Assert.False(first.Reader.TryRead(out _));
        Assert.False(otherWorkspace.Reader.TryRead(out _));

        using var reconnected = coordinator.Subscribe(selection.Definition.Id);
        Assert.True(reconnected.Reader.TryRead(out var recovered));
        Assert.Equal(latest, recovered);
        await coordinator.Stop(selection.Definition.Id, TestContext.Current.CancellationToken);
        Assert.True(first.Reader.TryRead(out var stopped));
        Assert.Equal("stopped", stopped.State);
        Assert.True(stopped.Sequence > latest.Sequence);
    }

    [Fact]
    public async Task NewJobsKeepSequenceAndCommitRevisionAndDisposalDoesNotStopJob()
    {
        var selection = Selection();
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var coordinator = Create(async (_, _, update, ct) =>
        {
            update(new WorkspaceIndexingUpdate("watching", "Saved.", IndexCommitted: true));
            updated.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        var subscription = coordinator.Subscribe(selection.Definition.Id);
        coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        subscription.Dispose();
        while (subscription.Reader.TryRead(out _)) { }
        Assert.True(subscription.Reader.Completion.IsCompleted);
        Assert.Equal("watching", coordinator.GetStatus(selection.Definition.Id).State);
        var stopped = await coordinator.Stop(selection.Definition.Id, TestContext.Current.CancellationToken);
        var restarted = coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        Assert.NotEqual(stopped.JobId, restarted.JobId);
        Assert.Equal(stopped.StreamId, restarted.StreamId);
        Assert.True(restarted.Sequence > stopped.Sequence);
        Assert.Equal(stopped.Revision, restarted.Revision);
    }

    [Fact]
    public async Task EventStreamEmitsTypedSnapshotAndCancellingReaderLeavesJobAlive()
    {
        var selection = Selection();
        await using var coordinator = Create((_, _, _, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var events = coordinator.Events(selection.Definition.Id, reading.Token).GetAsyncEnumerator(reading.Token);
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("status", events.Current.EventType);
        Assert.Equal("idle", events.Current.Data.State);
        Assert.Contains(":0", events.Current.EventId);
        coordinator.Start(selection, new StartWorkspaceIndexingRequest());
        Assert.True(await events.MoveNextAsync());
        Assert.Equal("indexing", events.Current.Data.State);
        await reading.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await events.MoveNextAsync());
        Assert.Equal("indexing", coordinator.GetStatus(selection.Definition.Id).State);
    }

    [Fact]
    public async Task StopRetainsCancelledAnalysisWithoutAdvancingGraphRevision()
    {
        var selection = Selection();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operationId = Guid.NewGuid();
        await using var coordinator = Create(async (_, _, update, ct) =>
        {
            var snapshots = new AnalysisSnapshotStore();
            snapshots.Notify(new AnalysisNotification(operationId, 1, DateTimeOffset.UtcNow,
                AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
            snapshots.Notify(new AnalysisNotification(operationId, 2, DateTimeOffset.UtcNow,
                AnalysisNotificationKind.SourceStarted, AnalysisOperationKind.Full, AnalysisPhase.Extraction,
                new AnalysisSource(WorkspaceSourceKind.CSharp, "App.sln")));
            update(new WorkspaceIndexingUpdate("indexing", "Analyzing sources...", Analysis: snapshots.Snapshot));
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                snapshots.Notify(new AnalysisNotification(operationId, 3, DateTimeOffset.UtcNow,
                    AnalysisNotificationKind.Cancelled, AnalysisOperationKind.Full));
                update(new WorkspaceIndexingUpdate("indexing", "Analysis cancelled.", Analysis: snapshots.Snapshot));
                throw;
            }
        });
        using var subscription = coordinator.Subscribe(selection.Definition.Id);
        coordinator.Start(selection, new StartWorkspaceIndexingRequest());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stopped = await coordinator.Stop(selection.Definition.Id, TestContext.Current.CancellationToken);

        Assert.Equal("stopped", stopped.State);
        Assert.Equal(0, stopped.Revision);
        Assert.NotNull(stopped.Analysis);
        Assert.Equal(operationId, stopped.Analysis.OperationId);
        Assert.Equal("cancelled", stopped.Analysis.State);
        Assert.NotNull(stopped.Analysis.CompletedAt);
        Assert.Equal("cancelled", Assert.Single(stopped.Analysis.Sources).State);
        Assert.True(subscription.Reader.TryRead(out var latest));
        Assert.Equal(stopped, latest);
    }

    private static WorkspaceSelection Selection()
        => new(new WorkspaceDefinition { Id = Guid.NewGuid(), Name = "test", RepositoryRoot = "/repo" },
            "/home/workspace", "/home/workspace/workspace.yaml", Mock.Of<IRepositoryWorkspace>());

    private static WorkspaceIndexingCoordinator Create(
        Func<WorkspaceSelection, StartWorkspaceIndexingRequest, Action<WorkspaceIndexingUpdate>, CancellationToken, Task> run)
        => new(run, CancellationToken.None, NullLogger<WorkspaceIndexingCoordinator>.Instance);
}
