using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using Spectre.Console;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class AnalyzeCommandWatchRoutingTests
{
    [Theory]
    [InlineData(false, false, false, 0, 1, 1)]
    [InlineData(true, false, false, 1, 1, 0)]
    [InlineData(false, true, false, 0, 2, 1)]
    [InlineData(false, true, true, 1, 2, 1)]
    public async Task WhenWatchReceivesResults_ThenPropagatesFailuresAndRecoversOnlyWhenNeeded(
        bool initialFailure, bool incrementalFailure, bool recoveryFailure, int expectedExit, int expectedIndexes, int expectedUpdates)
    {
        using var console = new TestConsole();
        var previousConsole = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            var index = new IndexHandler(initialFailure, recoveryFailure);
            var update = new UpdateHandler(incrementalFailure);
            var watcher = new Watcher();
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var app = Cli.Program.CreateCommandApp(console, services =>
            {
                services.AddWorkspaceFixture(root);
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>();
                services.RemoveAll<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>>();
                services.RemoveAll<IWorkspaceWatcher>();
                services.AddSingleton<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>(index);
                services.AddSingleton<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>>(update);
                services.AddSingleton<IWorkspaceWatcher>(watcher);
            }, enableFileLogging: false);
            var exit = await app.RunAsync(["analyze", "--workspace", "fixture", "--watch", "--repo-root", root, "--no-embeddings"], TestContext.Current.CancellationToken);
            Assert.Equal(expectedExit, exit);
            Assert.Equal(expectedIndexes, index.Calls);
            Assert.Equal(expectedUpdates, update.Calls);
            Assert.Equal(1, watcher.Calls);
            if (initialFailure || recoveryFailure)
            {
                Assert.DoesNotContain("Indexing complete.", console.Output[(console.Output.LastIndexOf("ERROR", StringComparison.Ordinal) + 1)..]);
                Assert.DoesNotContain("Watch mode recovery completed.", console.Output);
            }
        }
        finally { AnsiConsole.Console = previousConsole; }
    }

    [Fact]
    public async Task WhenInitialIndexFails_ThenReturnsNonzeroWithoutSuccessMessage()
    {
        using var console = new TestConsole();
        var previous = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var app = Cli.Program.CreateCommandApp(console, services =>
            {
                services.AddWorkspaceFixture(root);
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>();
                services.AddSingleton<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>(new IndexHandler(true, false));
            }, enableFileLogging: false);
            var exit = await app.RunAsync(["analyze", "--workspace", "fixture", "--repo-root", root, "--no-embeddings"], TestContext.Current.CancellationToken);
            Assert.Equal(1, exit);
            Assert.Contains("[fixture] failed", console.Output);
            Assert.DoesNotContain("Indexed workspace", console.Output);
        }
        finally { AnsiConsole.Console = previous; }
    }

    private sealed class IndexHandler(bool initialFailure, bool recoveryFailure) : ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>
    {
        public int Calls { get; private set; }
        public Task<Result<IndexTargetOutcome>> Handle(IndexTargetCommand command, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult((Calls == 1 ? initialFailure : recoveryFailure)
                ? Result.Fail<IndexTargetOutcome>("[fixture] failed")
                : Result.Ok(new IndexTargetOutcome(0, 0, 0, 0)));
        }
    }
    private sealed class UpdateHandler(bool failed) : ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>
    {
        public int Calls { get; private set; }
        public Task<Result<UpdateWorkspaceFilesOutcome>> Handle(UpdateWorkspaceFilesCommand command, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(failed ? Result.Fail<UpdateWorkspaceFilesOutcome>("incremental failed") : Result.Ok(new UpdateWorkspaceFilesOutcome(0, 0, 0)));
        }
    }
    private sealed class Watcher : IWorkspaceWatcher
    {
        public int Calls { get; private set; }
        public async Task Watch(string repositoryRoot, Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> onBatchChanged,
            CancellationToken ct, Func<CancellationToken, Task>? initialize = null, Action? onReady = null)
        {
            Calls++;
            if (initialize is not null) await initialize(ct);
            onReady?.Invoke();
            await onBatchChanged([new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: Path.Combine(repositoryRoot, "Feature.cs"))], ct);
        }
    }
}
