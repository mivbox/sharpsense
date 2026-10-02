using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceIndexingEventsTests
{
    [Fact]
    public async Task WhenSubscribers_ThenStartWithCurrentStateAndSlowReadersReceiveOnlyLatestState()
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
        first.Reader.TryRead(out var initial).Should().BeTrue();
        initial!.State.Should().Be("idle");
        initial.StreamId.Should().NotBeNull();
        using var otherWorkspace = coordinator.Subscribe(Guid.NewGuid());
        otherWorkspace.Reader.TryRead(out _).Should().BeTrue();

        coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        for (var index = 0; index < 1000; index++)
        {
            publish!(new WorkspaceIndexingUpdate(
                "indexing",
                $"Progress {index}"));
        }
        publish!(new WorkspaceIndexingUpdate("watching", "Saved.", IndexCommitted: true));
        first.Reader.TryRead(out var latest).Should().BeTrue();
        latest.Should().Be(coordinator.GetStatus(selection.Definition.Id));
        latest.Revision.Should().Be(1);
        (latest.Sequence > 1000).Should().BeTrue();
        first.Reader.TryRead(out _).Should().BeFalse();
        otherWorkspace.Reader.TryRead(out _).Should().BeFalse();

        using var reconnected = coordinator.Subscribe(selection.Definition.Id);
        reconnected.Reader.TryRead(out var recovered).Should().BeTrue();
        recovered.Should().Be(latest);
        await coordinator.Stop(selection.Definition.Id, TestContext.Current.CancellationToken);
        first.Reader.TryRead(out var stopped).Should().BeTrue();
        stopped!.State.Should().Be("stopped");
        (stopped.Sequence > latest.Sequence).Should().BeTrue();
    }

    [Fact]
    public async Task WhenNewJobs_ThenKeepSequenceAndCommitRevisionAndDisposalDoesNotStopJob()
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
        while (subscription.Reader.TryRead(out _))
        {
        }
        subscription.Reader.Completion.IsCompleted.Should().BeTrue();
        coordinator.GetStatus(selection.Definition.Id).State.Should().Be("watching");
        var stopped = await coordinator.Stop(selection.Definition.Id, TestContext.Current.CancellationToken);
        var restarted = coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        (restarted.JobId != stopped.JobId).Should().BeTrue();
        restarted.StreamId.Should().Be(stopped.StreamId);
        (restarted.Sequence > stopped.Sequence).Should().BeTrue();
        restarted.Revision.Should().Be(stopped.Revision);
    }

    [Fact]
    public async Task WhenEventStream_ThenEmitsTypedSnapshotAndCancellingReaderLeavesJobAlive()
    {
        var selection = Selection();
        await using var coordinator = Create((_, _, _, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct));
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var events = coordinator.Events(selection.Definition.Id, reading.Token)
            .GetAsyncEnumerator(reading.Token);
        (await events.MoveNextAsync()).Should().BeTrue();
        events.Current.EventType.Should().Be("status");
        events.Current.Data.State.Should().Be("idle");
        events.Current.EventId.Should().Contain(":0");
        coordinator.Start(selection, new StartWorkspaceIndexingRequest());
        (await events.MoveNextAsync()).Should().BeTrue();
        events.Current.Data.State.Should().Be("indexing");
        await reading.CancelAsync();
        await ((Func<Task>)(async () => await events.MoveNextAsync())).Should().ThrowAsync<OperationCanceledException>();
        coordinator.GetStatus(selection.Definition.Id).State.Should().Be("indexing");
    }

    [Fact]
    public async Task WhenStop_ThenRetainsCancelledAnalysisWithoutAdvancingGraphRevision()
    {
        var selection = Selection();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operationId = Guid.NewGuid();
        await using var coordinator = Create(async (_, _, update, ct) =>
        {
            var snapshots = new AnalysisSnapshotStore();
            snapshots.Notify(new AnalysisNotification(
                operationId,
                1,
                DateTimeOffset.UtcNow,
                AnalysisNotificationKind.Started,
                AnalysisOperationKind.Full));
            snapshots.Notify(new AnalysisNotification(
                operationId,
                2,
                DateTimeOffset.UtcNow,
                AnalysisNotificationKind.SourceStarted,
                AnalysisOperationKind.Full,
                AnalysisPhase.Extraction,
                new AnalysisSource(WorkspaceSourceKind.CSharp, "App.sln")));
            update(new WorkspaceIndexingUpdate("indexing", "Analyzing sources...", Analysis: snapshots.Snapshot));
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                snapshots.Notify(new AnalysisNotification(
                    operationId,
                    3,
                    DateTimeOffset.UtcNow,
                    AnalysisNotificationKind.Cancelled,
                    AnalysisOperationKind.Full));
                update(new WorkspaceIndexingUpdate("indexing", "Analysis cancelled.", Analysis: snapshots.Snapshot));
                throw;
            }
        });
        using var subscription = coordinator.Subscribe(selection.Definition.Id);
        coordinator.Start(selection, new StartWorkspaceIndexingRequest());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var stopped = await coordinator.Stop(selection.Definition.Id, TestContext.Current.CancellationToken);

        stopped!.State.Should().Be("stopped");
        stopped.Revision.Should().Be(0);
        stopped.Analysis.Should().NotBeNull();
        stopped.Analysis.OperationId.Should().Be(operationId);
        stopped.Analysis.State.Should().Be("cancelled");
        stopped.Analysis.CompletedAt.Should().NotBeNull();
        stopped.Analysis.Sources.Should().ContainSingle().Which.State.Should().Be("cancelled");
        subscription.Reader.TryRead(out var latest).Should().BeTrue();
        latest.Should().Be(stopped);
    }

    private static WorkspaceSelection Selection()
        => new(
            new WorkspaceDefinition
            {
                Id = Guid.NewGuid(),
                Name = "test",
                WorkspaceRoot = "/repo"
            },
            "/home/workspace",
            "/home/workspace/workspace.yaml",
            Mock.Of<IRepositoryWorkspace>());

    private static WorkspaceIndexingCoordinator Create(
        Func<WorkspaceSelection, StartWorkspaceIndexingRequest, Action<WorkspaceIndexingUpdate>, CancellationToken, Task> run)
        => new(run, TestContext.Current.CancellationToken, NullLogger<WorkspaceIndexingCoordinator>.Instance);
}
