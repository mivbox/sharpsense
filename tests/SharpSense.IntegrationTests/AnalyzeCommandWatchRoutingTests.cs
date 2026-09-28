using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
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
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpsense-watch-routing"));
            var app = Cli.Program.CreateCommandApp(
                console,
                services =>
            {
                services.AddWorkspaceFixture(root);
                services.AddScoped<IWorkspaceDatabaseInitializer, NoDatabaseInitializer>();
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>>();
                services.RemoveAll<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>>();
                services.RemoveAll<IWorkspaceWatcher>();
                services.AddSingleton<ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>>(index);
                services.AddSingleton<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>>(update);
                services.AddSingleton<IWorkspaceWatcher>(watcher);
            },
                enableFileLogging: false);
            var exit = await app.RunAsync(
                ["analyze", "--workspace", "fixture", "--watch", "--repo-root", root, "--no-embeddings"],
                TestContext.Current.CancellationToken);
            exit.Should().Be(expectedExit);
            index.Calls.Should().Be(expectedIndexes);
            update.Calls.Should().Be(expectedUpdates);
            watcher.Calls.Should().Be(1);
            if (initialFailure || recoveryFailure)
            {
                console.Output[(console.Output.LastIndexOf("ERROR", StringComparison.Ordinal) + 1)..].Should().NotContain("Indexing complete.");
                console.Output.Should().NotContain("Watch mode recovery completed.");
            }
        }
        finally
        {
            AnsiConsole.Console = previousConsole;
        }
    }

    [Fact]
    public async Task WhenInitialIndexFails_ThenReturnsNonzeroWithoutSuccessMessage()
    {
        using var console = new TestConsole();
        var previous = AnsiConsole.Console;
        AnsiConsole.Console = console;
        try
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpsense-watch-routing"));
            var app = Cli.Program.CreateCommandApp(
                console,
                services =>
            {
                services.AddWorkspaceFixture(root);
                services.AddScoped<IWorkspaceDatabaseInitializer, NoDatabaseInitializer>();
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>>();
                services.AddSingleton<ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>>(new IndexHandler(true, false));
            },
                enableFileLogging: false);
            var exit = await app.RunAsync(
                ["analyze", "--workspace", "fixture", "--repo-root", root, "--no-embeddings"],
                TestContext.Current.CancellationToken);
            exit.Should().Be(1);
            console.Output.Should().Contain("[fixture] failed");
            console.Output.Should().NotContain("Indexed workspace");
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    [Fact]
    public async Task WhenHostShutdown_ThenCancelsWatchAndReleasesWorkspaceLease()
    {
        using var console = new TestConsole();
        using var commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpsense-watch-routing"));
        IWorkspaceCatalog? catalog = null;
        WorkspaceSelection? selection = null;
        StoppingWatcher? watcher = null;
        var app = Cli.Program.CreateCommandApp(
            console,
            services =>
        {
            selection = services.AddWorkspaceFixture(root);
            services.AddScoped<IWorkspaceDatabaseInitializer, NoDatabaseInitializer>();
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>>();
            services.RemoveAll<IWorkspaceWatcher>();
            services.AddSingleton<ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>>(new IndexHandler(false, false));
            services.AddSingleton<IWorkspaceWatcher>(provider =>
            {
                catalog = provider.GetRequiredService<IWorkspaceCatalog>();
                watcher = new StoppingWatcher(provider.GetRequiredService<IHostApplicationLifetime>(), catalog, selection);

                return watcher;
            });
        },
            enableFileLogging: false);
        var run = app.RunAsync(["analyze", "--workspace", "fixture", "--watch", "--no-embeddings"], commandCancellation.Token);
        try
        {
            var exit = await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            exit.Should().Be(0);
            commandCancellation.IsCancellationRequested.Should().BeFalse();
            watcher.Should().NotBeNull();
            watcher.CleanedUp.Should().BeTrue();
            console.Output.Should().Contain("Analysis stopped.");
            using var reacquiredLease = catalog!.AcquireIndexLease(selection!);
        }
        finally
        {
            // Also clean up a failed regression instead of leaving its simulated watcher running.
            await commandCancellation.CancelAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    }

    private sealed class StoppingWatcher(IHostApplicationLifetime lifetime, IWorkspaceCatalog catalog, WorkspaceSelection selection) : IWorkspaceWatcher
    {
        public bool CleanedUp
        {
            get; private set;
        }

        public async Task Watch(string repositoryRoot, Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> onBatchChanged,
            CancellationToken ct, Func<CancellationToken, Task>? initialize = null, Action? onReady = null)
        {
            try
            {
                if (initialize is not null)
                {
                    await initialize(ct);
                }
                onReady?.Invoke();
                ((Action)(() => catalog.AcquireIndexLease(selection))).Should().ThrowExactly<WorkspaceIndexBusyException>();
                lifetime.StopApplication();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            finally
            {
                CleanedUp = true;
            }
        }
    }

    private sealed class NoDatabaseInitializer : IWorkspaceDatabaseInitializer
    {
        public Task Initialize(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class IndexHandler(bool initialFailure, bool recoveryFailure) : ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>
    {
        public int Calls
        {
            get; private set;
        }
        public Task<Result<IndexWorkspaceOutcome>> Handle(IndexWorkspaceCommand command, CancellationToken ct)
        {
            Calls++;

            return Task.FromResult((Calls == 1 ? initialFailure : recoveryFailure)
                ? Result.Fail<IndexWorkspaceOutcome>("[fixture] failed")
                : Result.Ok(new IndexWorkspaceOutcome(0, 0, 0, 0)));
        }
    }
    private sealed class UpdateHandler(bool failed) : ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>
    {
        public int Calls
        {
            get; private set;
        }
        public Task<Result<UpdateWorkspaceFilesOutcome>> Handle(UpdateWorkspaceFilesCommand command, CancellationToken ct)
        {
            Calls++;

            return Task.FromResult(failed
                ? Result.Fail<UpdateWorkspaceFilesOutcome>("incremental failed")
                : Result.Ok(new UpdateWorkspaceFilesOutcome(0, 0, 0)));
        }
    }
    private sealed class Watcher : IWorkspaceWatcher
    {
        public int Calls
        {
            get; private set;
        }
        public async Task Watch(string repositoryRoot, Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> onBatchChanged,
            CancellationToken ct, Func<CancellationToken, Task>? initialize = null, Action? onReady = null)
        {
            Calls++;
            if (initialize is not null)
            {
                await initialize(ct);
            }

            onReady?.Invoke();
            await onBatchChanged(
                [new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: Path.Combine(repositoryRoot, "Feature.cs"))],
                ct);
        }
    }
}
