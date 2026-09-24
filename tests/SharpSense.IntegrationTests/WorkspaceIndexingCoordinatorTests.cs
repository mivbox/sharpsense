using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceIndexingCoordinatorTests
{
    [Fact]
    public async Task WorkspacesRunIndependentlyAndDuplicateStartReusesActiveJob()
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
            update(new WorkspaceIndexingUpdate("watching", $"Watching {selection.Definition.Name}."));
            started[selection.Definition.Id].SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });

        var firstJob = coordinator.Start(first, new StartWorkspaceIndexingRequest(Watch: true));
        var duplicate = coordinator.Start(first, new StartWorkspaceIndexingRequest());
        var secondJob = coordinator.Start(second, new StartWorkspaceIndexingRequest(Watch: true));
        await Task.WhenAll(started.Values.Select(static completion => completion.Task))
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(firstJob.JobId, duplicate.JobId);
        Assert.NotEqual(firstJob.JobId, secondJob.JobId);
        Assert.Equal(1, counts[first.Definition.Id]);
        Assert.Equal(1, counts[second.Definition.Id]);
        Assert.Throws<WorkspaceBusyException>(() => coordinator.UpdateWhileIdle(first.Definition.Id, () => "updated"));
        var stopped = await coordinator.Stop(first.Definition.Id, TestContext.Current.CancellationToken);
        Assert.Equal("stopped", stopped.State);
        Assert.NotNull(stopped.CompletedAt);
        Assert.Equal("watching", coordinator.GetStatus(second.Definition.Id).State);
        Assert.Equal("updated", coordinator.UpdateWhileIdle(first.Definition.Id, () => "updated"));

        await coordinator.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal("stopped", coordinator.GetStatus(second.Definition.Id).State);
    }

    [Fact]
    public async Task HostCancellationStopsSessionsAndWaitsForTheirCleanup()
    {
        var selection = Selection("app");
        using var stopping = new CancellationTokenSource();
        var started = Completion();
        var cleanedUp = Completion();
        await using var coordinator = new WorkspaceIndexingCoordinator(async (_, _, _, ct) =>
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
        }, stopping.Token, NullLogger<WorkspaceIndexingCoordinator>.Instance);
        coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await stopping.CancelAsync();
        await coordinator.StopAsync(TestContext.Current.CancellationToken);

        Assert.True(cleanedUp.Task.IsCompletedSuccessfully);
        Assert.Equal("stopped", coordinator.GetStatus(selection.Definition.Id).State);
        Assert.Throws<InvalidOperationException>(() => coordinator.Start(selection, new StartWorkspaceIndexingRequest()));
    }

    [Fact]
    public async Task FailedJobRetainsErrorAndCanBeStartedAgain()
    {
        var selection = Selection("app");
        var attempts = 0;
        await using var coordinator = Create((_, _, _, _) =>
            Interlocked.Increment(ref attempts) == 1
                ? Task.FromException(new InvalidOperationException("Selected source could not be read."))
                : Task.CompletedTask);

        var failed = coordinator.Start(selection, new StartWorkspaceIndexingRequest());
        await WaitForState(coordinator, selection.Definition.Id, "failed");
        Assert.Contains("Selected source could not be read.", coordinator.GetStatus(selection.Definition.Id).Diagnostics);

        var restarted = coordinator.Start(selection, new StartWorkspaceIndexingRequest());
        await WaitForState(coordinator, selection.Definition.Id, "completed");
        Assert.NotEqual(failed.JobId, restarted.JobId);
        Assert.Empty(coordinator.GetStatus(selection.Definition.Id).Diagnostics);
    }

    [Fact]
    public async Task WatchDiagnosticsAreBoundedAndProgressKeepsWorkspaceIdentity()
    {
        var selection = Selection("app");
        var updated = Completion();
        await using var coordinator = Create(async (_, _, update, ct) =>
        {
            update(new WorkspaceIndexingUpdate("watching", "Initial index saved.", IndexCommitted: true));
            update(new WorkspaceIndexingUpdate("indexing", "Reindexing changes..."));
            update(new WorkspaceIndexingUpdate("watching", new string('m', 3000), 3, 10,
                [.. Enumerable.Range(0, 30).Select(index => $"{index}:" + new string('d', 3000))], IndexCommitted: true));
            updated.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        });
        coordinator.Start(selection, new StartWorkspaceIndexingRequest(Watch: true));
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var status = coordinator.GetStatus(selection.Definition.Id);
        Assert.Equal(selection.Definition.Id, status.WorkspaceId);
        Assert.Equal(3, status.CompletedItems);
        Assert.Equal(10, status.TotalItems);
        Assert.Equal(2, status.Revision);
        Assert.Equal(2000, status.Message!.Length);
        Assert.Equal(20, status.Diagnostics.Count);
        Assert.All(status.Diagnostics, diagnostic => Assert.True(diagnostic.Length <= 2000));
    }

    private static WorkspaceIndexingCoordinator Create(
        Func<WorkspaceSelection, StartWorkspaceIndexingRequest, Action<WorkspaceIndexingUpdate>, CancellationToken, Task> run)
        => new(run, CancellationToken.None, NullLogger<WorkspaceIndexingCoordinator>.Instance);

    private static TaskCompletionSource Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static WorkspaceSelection Selection(string name)
        => new(new WorkspaceDefinition { Id = Guid.NewGuid(), Name = name, RepositoryRoot = "/repo" },
            "/home/workspace", "/home/workspace/workspace.yaml", Mock.Of<IRepositoryWorkspace>());

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
