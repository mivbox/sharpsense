using System.Collections.ObjectModel;
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
    public async Task WhenWatchStarts_ThenConfiguresFileAndDirectoryWatchers()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, fileWatcher, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
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

        fileWatcher.Object.IncludeSubdirectories.Should().BeTrue();
        fileWatcher.Object.InternalBufferSize.Should().Be(64 * 1024);
        fileWatcher.Object.NotifyFilter.Should().Be(
            NotifyFilters.CreationTime |
            NotifyFilters.FileName |
            NotifyFilters.LastWrite |
            NotifyFilters.Size);
        fileWatcher.Object.Filters.Should().Equal("*.cs", "*.ts", "*.tsx", "*.md", "*.markdown", "*.mdown", "*.mkd", "*.yaml", "*.yml");
        directoryWatcher.Object.IncludeSubdirectories.Should().BeTrue();
        directoryWatcher.Object.InternalBufferSize.Should().Be(16 * 1024);
        directoryWatcher.Object.NotifyFilter.Should().Be(NotifyFilters.DirectoryName);
        directoryWatcher.Object.Filters.Should().BeEmpty();

        fileWatcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/src", "Feature.cs"));

        await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;
    }

    [Fact]
    public async Task WhenRelevantFilesChangeBeforeDebounceWindow_ThenEmitsSingleBatch()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        fileSystem.AddFile("/repo/docs/Guide.md", new MockFileData("# Guide"));
        var (workspaceWatcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
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

        fileWatcher.Raise(
            candidate => candidate.Created += null,
            new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo/src", "Feature.cs"));
        fileWatcher.Raise(
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
    public async Task WhenGitPathsChange_ThenSkipsFastPathEvents()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/.git/config", new MockFileData("[core]"));
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
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

        fileWatcher.Raise(
            candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/.git", "config"));
        fileWatcher.Raise(
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
        fileSystem.AddFile("/repo/src/NewFeature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
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

        fileWatcher.Raise(
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
    public async Task WhenRelevantFileIsRenamedIntoIgnoredDirectory_ThenReportsDelete()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        fileSystem.AddFile("/repo/bin/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
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

        fileWatcher.Raise(
            candidate => candidate.Renamed += null,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, "/repo", "bin/Feature.cs", "src/Feature.cs"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        changes.Should().ContainSingle();
        changes[0].ActionType.Should().Be(WorkspaceFileChangeAction.Deleted);
        changes[0].OldPath.Should().Be("/repo/src/Feature.cs");
        changes[0].NewPath.Should().BeNull();
    }

    [Fact]
    public async Task WhenIgnoredFileIsRenamedIntoRelevantDirectory_ThenReportsAdd()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/bin/Feature.cs", new MockFileData("public sealed class Feature { }"));
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
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

        fileWatcher.Raise(
            candidate => candidate.Renamed += null,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, "/repo", "src/Feature.cs", "bin/Feature.cs"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        changes.Should().ContainSingle();
        changes[0].ActionType.Should().Be(WorkspaceFileChangeAction.Added);
        changes[0].OldPath.Should().BeNull();
        changes[0].NewPath.Should().Be("/repo/src/Feature.cs");
    }

    [Fact]
    public async Task WhenRelevantDirectoryIsRenamed_ThenReportsDirectoryRenamePaths()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/OldFeature/Feature.cs", new MockFileData("public sealed class Feature { }"));
        fileSystem.AddFile("/repo/src/NewFeature/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, _, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
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

        directoryWatcher.Raise(
            candidate => candidate.Renamed += null,
            new RenamedEventArgs(WatcherChangeTypes.Renamed, "/repo/src", "NewFeature", "OldFeature"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        changes.Should().ContainSingle();
        changes[0].ActionType.Should().Be(WorkspaceFileChangeAction.DirectoryRenamed);
        changes[0].OldPath.Should().Be("/repo/src/OldFeature");
        changes[0].NewPath.Should().Be("/repo/src/NewFeature");
    }

    [Fact]
    public async Task WhenRelevantDirectoryIsDeleted_ThenEmitsDirectoryDeleted()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/FeatureFolder/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, _, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
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

        directoryWatcher.Raise(
            candidate => candidate.Deleted += null,
            new FileSystemEventArgs(WatcherChangeTypes.Deleted, "/repo/src", "FeatureFolder"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        changes.Should().ContainSingle();
        changes[0].ActionType.Should().Be(WorkspaceFileChangeAction.DirectoryDeleted);
        changes[0].OldPath.Should().Be("/repo/src/FeatureFolder");
    }

    [Fact]
    public async Task WhenIgnoredDirectoryIsDeletedWithMixedCase_ThenSkipsIgnoredPaths()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/src/NODE_MODULES/Generated.cs", new MockFileData("public sealed class Generated { }"));
        fileSystem.AddFile("/repo/src/Feature.cs", new MockFileData("public sealed class Feature { }"));
        var (workspaceWatcher, fileWatcher, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
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

        directoryWatcher.Raise(
            candidate => candidate.Deleted += null,
            new FileSystemEventArgs(WatcherChangeTypes.Deleted, "/repo/src", "NODE_MODULES"));
        watchTask.IsCompleted.Should().BeFalse();
        fileWatcher.Raise(
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
    public async Task WhenFileSystemWatcherSignalsFatalError_ThenThrows()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var (workspaceWatcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var expectedException = new IOException("The watcher buffer overflowed.");

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            static (_, _) => Task.CompletedTask,
            cancellation.Token);

        fileWatcher.Raise(candidate => candidate.Error += null, new ErrorEventArgs(expectedException));

        Func<Task> act = async () => await watchTask;

        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.WithMessage("Workspace watcher encountered a fatal file system watcher error.");
        exception.Which.InnerException.Should().BeSameAs(expectedException);
    }

    [Fact]
    public async Task WhenDirectoryWatcherSignalsFatalError_ThenThrows()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var (workspaceWatcher, _, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var expectedException = new IOException("The directory watcher buffer overflowed.");

        var watchTask = workspaceWatcher.Watch(
            "/repo",
            static (_, _) => Task.CompletedTask,
            cancellation.Token);

        directoryWatcher.Raise(candidate => candidate.Error += null, new ErrorEventArgs(expectedException));

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

    private static (WorkspaceWatcher WorkspaceWatcher, Mock<IFileSystemWatcher> FileWatcher, Mock<IFileSystemWatcher> DirectoryWatcher) CreateWorkspaceWatcher(MockFileSystem fileSystem)
    {
        var fileWatcher = CreateWatcherMock();
        var directoryWatcher = CreateWatcherMock();

        var watcherFactory = new Mock<IFileSystemWatcherFactory>(MockBehavior.Strict);
        watcherFactory.SetupSequence(candidate => candidate.New("/repo"))
            .Returns(fileWatcher.Object)
            .Returns(directoryWatcher.Object);

        return (new WorkspaceWatcher(fileSystem, watcherFactory.Object), fileWatcher, directoryWatcher);
    }

    private static Mock<IFileSystemWatcher> CreateWatcherMock()
    {
        var watcher = new Mock<IFileSystemWatcher>(MockBehavior.Strict);
        watcher.SetupProperty(candidate => candidate.EnableRaisingEvents);
        watcher.SetupProperty(candidate => candidate.IncludeSubdirectories);
        watcher.SetupProperty(candidate => candidate.InternalBufferSize);
        watcher.SetupProperty(candidate => candidate.NotifyFilter);
        watcher.SetupGet(candidate => candidate.Filters)
            .Returns(new Collection<string>());
        watcher.Setup(candidate => candidate.Dispose());
        return watcher;
    }
}
