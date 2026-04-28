using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Indexing.Watching;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing.Watching;

public sealed class WorkspaceWatcherTests
{
    [Fact]
    public async Task WhenRelevantFilesChangeBeforeDebounceWindow_ThenEmitsSingleBatch()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        fileSystem.AddFile("/repo/docs/Guide.md", new MockFileData("# Guide"));
        var (workspaceWatcher, watcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            (changes, _) =>
            {
                callbackCount++;
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        watcher.Raise(
            candidate => candidate.Created += null,
            new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo/src", "Feature.cs"));
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/docs", "Guide.md"));

        var observedChanges = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        callbackCount.Should().Be(1);
        observedChanges.Should().HaveCount(2);
        observedChanges[0].ActionType.Should().Be(WorkspaceFileChangeAction.Added);
        observedChanges[0].NewPath.Should().Be("/repo/src/Feature.cs");
        observedChanges[1].ActionType.Should().Be(WorkspaceFileChangeAction.Modified);
        observedChanges[1].NewPath.Should().Be("/repo/docs/Guide.md");
    }

    [Fact]
    public async Task WhenChangesOccurUnderIgnoredDirectories_ThenSkipsIgnoredPaths()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/.GIT/Ignored.cs", new MockFileData("public sealed class Ignored { }"));
        fileSystem.AddFile("/repo/src/BIN/Debug/Generated.cs", new MockFileData("public sealed class Generated { }"));
        fileSystem.AddFile("/repo/src/Obj/Generated.cs", new MockFileData("public sealed class Generated { }"));
        fileSystem.AddFile("/repo/src/.VS/Generated.cs", new MockFileData("public sealed class Generated { }"));
        fileSystem.AddFile("/repo/src/.IDEA/Generated.md", new MockFileData("# Generated"));
        fileSystem.AddFile("/repo/src/NODE_MODULES/Generated.cs", new MockFileData("public sealed class Generated { }"));
        fileSystem.AddFile("/repo/src/testresults/Generated.md", new MockFileData("# Generated"));
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, watcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            (changes, _) =>
            {
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src/.GIT", "Ignored.cs"));
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src/BIN/Debug", "Generated.cs"));
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src/Obj", "Generated.cs"));
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src/.VS", "Generated.cs"));
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src/.IDEA", "Generated.md"));
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src/NODE_MODULES", "Generated.cs"));
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src/testresults", "Generated.md"));
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src", "Feature.cs"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        changes.Should().ContainSingle();
        changes[0].ActionType.Should().Be(WorkspaceFileChangeAction.Modified);
        changes[0].NewPath.Should().Be("/repo/src/Feature.cs");
    }

    [Fact]
    public async Task WhenRelevantFileIsRenamed_ThenReportsRenamePaths()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/OldFeature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, watcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            (changes, _) =>
            {
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        fileSystem.AddFile("/repo/src/NewFeature.cs", new MockFileData("public sealed class Feature { }"));
        watcher.Raise(
            candidate => candidate.Renamed += null,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, "/repo/src", "NewFeature.cs", "OldFeature.cs"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        changes.Should().ContainSingle();
        changes[0].ActionType.Should().Be(WorkspaceFileChangeAction.Renamed);
        changes[0].OldPath.Should().Be("/repo/src/OldFeature.cs");
        changes[0].NewPath.Should().Be("/repo/src/NewFeature.cs");
    }

    [Fact]
    public async Task WhenRelevantDirectoryIsRenamed_ThenReportsContainedFileRenamePaths()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/OldFeature/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, watcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            (changes, _) =>
            {
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        fileSystem.AddFile("/repo/src/NewFeature/Feature.cs", new MockFileData("public sealed class Feature { }"));
        watcher.Raise(
            candidate => candidate.Renamed += null,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, "/repo/src", "NewFeature", "OldFeature"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        changes.Should().ContainSingle();
        changes[0].ActionType.Should().Be(WorkspaceFileChangeAction.Renamed);
        changes[0].OldPath.Should().Be("/repo/src/OldFeature/Feature.cs");
        changes[0].NewPath.Should().Be("/repo/src/NewFeature/Feature.cs");
    }

    [Fact]
    public async Task WhenIgnoredDirectoryIsDeletedWithMixedCase_ThenDoesNotTriggerFatalRecovery()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/NODE_MODULES/Generated.cs", new MockFileData("public sealed class Generated { }"));
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, watcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            (changes, _) =>
            {
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        watcher.Raise(
            candidate => candidate.Deleted += null,
            new FileSystemEventArgs(WatcherChangeTypes.Deleted, "/repo/src", "NODE_MODULES"));
        watchTask.IsCompleted.Should().BeFalse();
        watcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src", "Feature.cs"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        changes.Should().ContainSingle();
        changes[0].ActionType.Should().Be(WorkspaceFileChangeAction.Modified);
        changes[0].NewPath.Should().Be("/repo/src/Feature.cs");
    }

    [Fact]
    public async Task WhenRelevantDirectoryIsDeleted_ThenThrows()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/FeatureFolder/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, watcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            static (_, _) => Task.CompletedTask,
            cancellation.Token);

        watcher.Raise(
            candidate => candidate.Deleted += null,
            new FileSystemEventArgs(WatcherChangeTypes.Deleted, "/repo/src", "FeatureFolder"));

        Func<Task> act = async () => await watchTask;

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.WithMessage("Workspace watcher encountered a fatal file system watcher error.");
        exception.Which.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Be("Workspace watcher detected a directory deletion that requires a full reindex.");
    }

    [Fact]
    public async Task WhenFileSystemWatcherSignalsFatalError_ThenThrows()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var (workspaceWatcher, watcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var expectedException = new IOException("The watcher buffer overflowed.");

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            static (_, _) => Task.CompletedTask,
            cancellation.Token);

        watcher.Raise(candidate => candidate.Error += null, new ErrorEventArgs(expectedException));

        Func<Task> act = async () => await watchTask;

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.WithMessage("Workspace watcher encountered a fatal file system watcher error.");
        exception.Which.InnerException.Should().BeSameAs(expectedException);
    }

    private static MockFileSystem CreateRepositoryFileSystem()
        => new(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, "/repo");

    private static (WorkspaceWatcher WorkspaceWatcher, Mock<IFileSystemWatcher> Watcher) CreateWorkspaceWatcher(MockFileSystem fileSystem)
    {
        var watcher = new Mock<IFileSystemWatcher>(MockBehavior.Strict);
        watcher.SetupProperty(candidate => candidate.EnableRaisingEvents);
        watcher.SetupProperty(candidate => candidate.IncludeSubdirectories);
        watcher.SetupProperty(candidate => candidate.InternalBufferSize);
        watcher.SetupProperty(candidate => candidate.NotifyFilter);
        watcher.Setup(candidate => candidate.Dispose());

        var watcherFactory = new Mock<IFileSystemWatcherFactory>(MockBehavior.Strict);
        watcherFactory.Setup(candidate => candidate.New("/repo"))
            .Returns(watcher.Object);

        return (new WorkspaceWatcher(fileSystem, watcherFactory.Object), watcher);
    }
}
