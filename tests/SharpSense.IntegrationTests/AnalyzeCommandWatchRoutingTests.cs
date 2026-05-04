using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class AnalyzeCommandWatchRoutingTests
{
    [Fact]
    public async Task WhenAnalyzeRunsInWatchMode_ThenRoutesChangeBatchThroughWorkspaceWatcher()
    {
        var repositoryRoot = GetRepositoryRoot();
        using var console = new TestConsole();
        var indexCommands = new List<IndexTargetCommand>();
        var updateCommands = new List<UpdateWorkspaceFilesCommand>();
        var repositoryRoots = new List<string>();
        var indexHandler = new Mock<ICommandHandler<IndexTargetCommand>>(MockBehavior.Strict);
        indexHandler.Setup(handler => handler.Handle(It.IsAny<IndexTargetCommand>(), It.IsAny<CancellationToken>()))
            .Returns<IndexTargetCommand, CancellationToken>((command, _) =>
            {
                indexCommands.Add(command);
                return Task.CompletedTask;
            });
        var updateHandler = new Mock<ICommandHandler<UpdateWorkspaceFilesCommand>>(MockBehavior.Strict);
        updateHandler.Setup(handler => handler.Handle(It.IsAny<UpdateWorkspaceFilesCommand>(), It.IsAny<CancellationToken>()))
            .Returns<UpdateWorkspaceFilesCommand, CancellationToken>((command, _) =>
            {
                updateCommands.Add(command);
                return Task.CompletedTask;
            });
        var workspaceWatcher = new Mock<IWorkspaceWatcher>(MockBehavior.Strict);
        workspaceWatcher.Setup(watcher => watcher.Watch(
                It.IsAny<string>(),
                It.IsAny<Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task>, CancellationToken>(
                (configuredRepositoryRoot, onBatchChanged, ct) =>
                {
                    repositoryRoots.Add(configuredRepositoryRoot);
                    return onBatchChanged(
                        [
                            new WorkspaceFileChange(
                                WorkspaceFileChangeAction.Modified,
                                NewPath: "/repo/src/Feature.cs")
                        ],
                        ct);
                });
        var app = Cli.Program.CreateCommandApp(console, services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ICommandHandler<IndexTargetCommand>>();
            services.RemoveAll<ICommandHandler<UpdateWorkspaceFilesCommand>>();
            services.RemoveAll<IWorkspaceWatcher>();
            services.AddSingleton<ICommandHandler<IndexTargetCommand>>(indexHandler.Object);
            services.AddSingleton<ICommandHandler<UpdateWorkspaceFilesCommand>>(updateHandler.Object);
            services.AddSingleton<IWorkspaceWatcher>(workspaceWatcher.Object);
        });

        var exitCode = await app.RunAsync(
            ["analyze", "SharpSense.sln", "--watch", "--repo-root", repositoryRoot, "--no-embeddings"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        indexCommands.Should().ContainSingle();
        updateCommands.Should().ContainSingle();
        repositoryRoots.Should().ContainSingle().Which.Should().Be(
            Path.TrimEndingDirectorySeparator(repositoryRoot));
        updateCommands[0].ChangedFiles.Should().ContainSingle();
        updateCommands[0].ChangedFiles![0].NewPath.Should().Be("/repo/src/Feature.cs");
    }

    [Fact]
    public async Task WhenWorkspaceWatcherFails_ThenReindexesAndRestartsWatching()
    {
        var repositoryRoot = GetRepositoryRoot();
        using var console = new TestConsole();
        var indexCommands = new List<IndexTargetCommand>();
        var updateCommands = new List<UpdateWorkspaceFilesCommand>();
        var repositoryRoots = new List<string>();
        var watchSteps = new Queue<Func<string, Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task>, CancellationToken, Task>>(
            [
                static (_, _, _) => Task.FromException(new InvalidOperationException("fatal watcher error")),
                static (_, onBatchChanged, ct) => onBatchChanged(
                    [
                        new WorkspaceFileChange(
                            WorkspaceFileChangeAction.Modified,
                            NewPath: "/repo/src/Recovered.cs")
                    ],
                    ct)
            ]);
        var indexHandler = new Mock<ICommandHandler<IndexTargetCommand>>(MockBehavior.Strict);
        indexHandler.Setup(handler => handler.Handle(It.IsAny<IndexTargetCommand>(), It.IsAny<CancellationToken>()))
            .Returns<IndexTargetCommand, CancellationToken>((command, _) =>
            {
                indexCommands.Add(command);
                return Task.CompletedTask;
            });
        var updateHandler = new Mock<ICommandHandler<UpdateWorkspaceFilesCommand>>(MockBehavior.Strict);
        updateHandler.Setup(handler => handler.Handle(It.IsAny<UpdateWorkspaceFilesCommand>(), It.IsAny<CancellationToken>()))
            .Returns<UpdateWorkspaceFilesCommand, CancellationToken>((command, _) =>
            {
                updateCommands.Add(command);
                return Task.CompletedTask;
            });
        var workspaceWatcher = new Mock<IWorkspaceWatcher>(MockBehavior.Strict);
        workspaceWatcher.Setup(watcher => watcher.Watch(
                It.IsAny<string>(),
                It.IsAny<Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task>, CancellationToken>(
                (configuredRepositoryRoot, onBatchChanged, ct) =>
                {
                    repositoryRoots.Add(configuredRepositoryRoot);
                    return watchSteps.Count == 0
                        ? Task.CompletedTask
                        : watchSteps.Dequeue().Invoke(configuredRepositoryRoot, onBatchChanged, ct);
                });
        var app = Cli.Program.CreateCommandApp(console, services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ICommandHandler<IndexTargetCommand>>();
            services.RemoveAll<ICommandHandler<UpdateWorkspaceFilesCommand>>();
            services.RemoveAll<IWorkspaceWatcher>();
            services.AddSingleton<ICommandHandler<IndexTargetCommand>>(indexHandler.Object);
            services.AddSingleton<ICommandHandler<UpdateWorkspaceFilesCommand>>(updateHandler.Object);
            services.AddSingleton<IWorkspaceWatcher>(workspaceWatcher.Object);
        });

        var exitCode = await app.RunAsync(
            ["analyze", "SharpSense.sln", "--watch", "--repo-root", repositoryRoot, "--no-embeddings"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        indexCommands.Should().HaveCount(2);
        updateCommands.Should().ContainSingle();
        repositoryRoots.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhenAnalyzeRunsInWatchModeWithCanonicalWorkspaceRoot_ThenWatcherUsesWorkspaceRootPath()
    {
        var configuredRepositoryRoot = "/tmp/sharpsense-fixture";
        var canonicalRepositoryRoot = "/private/tmp/sharpsense-fixture";
        using var console = new TestConsole();
        var watchedRoots = new List<string>();
        var indexHandler = new Mock<ICommandHandler<IndexTargetCommand>>(MockBehavior.Strict);
        indexHandler.Setup(handler => handler.Handle(It.IsAny<IndexTargetCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var updateHandler = new Mock<ICommandHandler<UpdateWorkspaceFilesCommand>>(MockBehavior.Strict);
        updateHandler.Setup(handler => handler.Handle(It.IsAny<UpdateWorkspaceFilesCommand>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var workspaceWatcher = new Mock<IWorkspaceWatcher>(MockBehavior.Strict);
        workspaceWatcher.Setup(watcher => watcher.Watch(
                It.IsAny<string>(),
                It.IsAny<Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task>>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task>, CancellationToken>(
                (repositoryRoot, _, _) =>
                {
                    watchedRoots.Add(repositoryRoot);
                    return Task.CompletedTask;
                });
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace.SetupGet(candidate => candidate.RootPath)
            .Returns(canonicalRepositoryRoot);
        var app = Cli.Program.CreateCommandApp(console, services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ICommandHandler<IndexTargetCommand>>();
            services.RemoveAll<ICommandHandler<UpdateWorkspaceFilesCommand>>();
            services.RemoveAll<IWorkspaceWatcher>();
            services.RemoveAll<IRepositoryWorkspace>();
            services.AddSingleton<ICommandHandler<IndexTargetCommand>>(indexHandler.Object);
            services.AddSingleton<ICommandHandler<UpdateWorkspaceFilesCommand>>(updateHandler.Object);
            services.AddSingleton<IWorkspaceWatcher>(workspaceWatcher.Object);
            services.AddSingleton<IRepositoryWorkspace>(repositoryWorkspace.Object);
        });

        var exitCode = await app.RunAsync(
            ["analyze", "SharpSense.sln", "--watch", "--repo-root", configuredRepositoryRoot, "--no-embeddings"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        watchedRoots.Should().ContainSingle().Which.Should().Be(canonicalRepositoryRoot);
    }

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
}
