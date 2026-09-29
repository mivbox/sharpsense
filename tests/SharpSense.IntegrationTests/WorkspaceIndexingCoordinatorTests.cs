using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Storage;
using System.Collections.Concurrent;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceIndexingCoordinatorTests
{
    [Fact]
    public async Task WhenWorkspacesRunIndependentlyAndDuplicate_ThenStartReusesActiveJob()
    {
        var first = Selection("first");
        var second = Selection("second");
        var started = new ConcurrentDictionary<Guid, TaskCompletionSource>();
        started[first.Definition.Id] = Completion();
        started[second.Definition.Id] = Completion();
        var counts = new ConcurrentDictionary<Guid, int>();
        await using var coordinator = Create(async (selection, _, update, ct) =>
        {
            counts.AddOrUpdate(selection.Definition.Id, 1, static (_, count) => count + 1);
            update(new WorkspaceIndexingUpdate(
                "watching",
                $"Watching {selection.Definition.Name}."));
            started[selection.Definition.Id].SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });

        var firstJob = coordinator.Start(first, new StartWorkspaceIndexingRequest(Watch: true));
        var duplicate = coordinator.Start(first, new StartWorkspaceIndexingRequest());
        var secondJob = coordinator.Start(second, new StartWorkspaceIndexingRequest(Watch: true));
        await Task.WhenAll(started.Values.Select(static completion => completion.Task))
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        duplicate.JobId.Should().Be(firstJob.JobId);
        (secondJob.JobId != firstJob.JobId).Should().BeTrue();
        counts[first.Definition.Id].Should().Be(1);
        counts[second.Definition.Id].Should().Be(1);
        ((Action)(() => coordinator.UpdateWhileIdle(first.Definition.Id, () => "updated"))).Should().ThrowExactly<WorkspaceBusyException>();
        var stopped = await coordinator.Stop(first.Definition.Id, TestContext.Current.CancellationToken);
        stopped!.State.Should().Be("stopped");
        stopped.CompletedAt.Should().NotBeNull();
        coordinator.GetStatus(second.Definition.Id).State.Should().Be("watching");
        coordinator.UpdateWhileIdle(first.Definition.Id, () => "updated").Should().Be("updated");

        await coordinator.StopAsync(TestContext.Current.CancellationToken);
        coordinator.GetStatus(second.Definition.Id).State.Should().Be("stopped");
    }

    [Fact]
    public async Task WhenHostCancellation_ThenStopsSessionsAndWaitsForTheirCleanup()
    {
        var selection = Selection("app");
        using var stopping = new CancellationTokenSource();
        var started = Completion();
        var cleanedUp = Completion();
        await using var coordinator = new WorkspaceIndexingCoordinator(
            async (_, _, _, ct) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                cleanedUp.SetResult();
            }
        },
            stopping.Token,
            NullLogger<WorkspaceIndexingCoordinator>.Instance);
        coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await stopping.CancelAsync();
        await coordinator.StopAsync(TestContext.Current.CancellationToken);

        cleanedUp.Task.IsCompletedSuccessfully.Should().BeTrue();
        coordinator.GetStatus(selection.Definition.Id).State.Should().Be("stopped");
        ((Action)(() => coordinator.Start(selection, new StartWorkspaceIndexingRequest()))).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public async Task WhenFailedJob_ThenRetainsErrorAndCanBeStartedAgain()
    {
        var selection = Selection("app");
        var attempts = 0;
        await using var coordinator = Create((_, _, _, _) =>
            Interlocked.Increment(ref attempts) == 1
            ? Task.FromException(new InvalidOperationException("Selected source could not be read."))
            : Task.CompletedTask);

        var failed = coordinator.Start(selection, new StartWorkspaceIndexingRequest());
        await WaitForState(coordinator, selection.Definition.Id, "failed");
        coordinator.GetStatus(selection.Definition.Id).Diagnostics.Should().Contain("Selected source could not be read.");

        var restarted = coordinator.Start(selection, new StartWorkspaceIndexingRequest());
        await WaitForState(coordinator, selection.Definition.Id, "completed");
        (restarted.JobId != failed.JobId).Should().BeTrue();
        coordinator.GetStatus(selection.Definition.Id).Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenWatchDiagnostics_ThenAreBoundedAndProgressKeepsWorkspaceIdentity()
    {
        var selection = Selection("app");
        var updated = Completion();
        await using var coordinator = Create(async (_, _, update, ct) =>
        {
            update(new WorkspaceIndexingUpdate("watching", "Initial index saved.", IndexCommitted: true));
            update(new WorkspaceIndexingUpdate("indexing", "Reindexing changes..."));
            update(new WorkspaceIndexingUpdate(
                "watching",
                new string('m', 3000),
                3,
                10,
                [.. Enumerable.Range(0, 30)
                    .Select(index => $"{index}:" + new string('d', 3000))],
                IndexCommitted: true));
            updated.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var status = coordinator.GetStatus(selection.Definition.Id);
        status.WorkspaceId.Should().Be(selection.Definition.Id);
        status.CompletedItems.Should().Be(3);
        status.TotalItems.Should().Be(10);
        status.Revision.Should().Be(2);
        status.Message!.Length.Should().Be(2000);
        status.Diagnostics.Count.Should().Be(20);
        status.Diagnostics.Should().AllSatisfy(diagnostic => (diagnostic.Length <= 2000).Should().BeTrue());
    }

    private static WorkspaceIndexingCoordinator Create(
        Func<WorkspaceSelection, StartWorkspaceIndexingRequest, Action<WorkspaceIndexingUpdate>, CancellationToken, Task> run)
        => new(run, CancellationToken.None, NullLogger<WorkspaceIndexingCoordinator>.Instance);

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static WorkspaceSelection Selection(string name)
        => new(
            new WorkspaceDefinition
            {
                Id = Guid.NewGuid(),
                Name = name,
                WorkspaceRoot = "/repo"
            },
            "/home/workspace",
            "/home/workspace/workspace.yaml",
            Mock.Of<IRepositoryWorkspace>());

    private static async Task WaitForState(WorkspaceIndexingCoordinator coordinator, Guid workspaceId, string state)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (coordinator.GetStatus(workspaceId).State != state)
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
