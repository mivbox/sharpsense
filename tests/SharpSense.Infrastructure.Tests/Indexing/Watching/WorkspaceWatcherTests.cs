using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Indexing.Watching;

namespace SharpSense.Infrastructure.Tests.Indexing.Watching;

public sealed class WorkspaceWatcherTests
{
    [Fact]
    public async Task WhenRelevantFilesChangeBeforeDebounceWindow_ThenEmitsSingleBatch()
    {
        using var repositoryRoot = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watcher = new TestFileSystemWatcher(repositoryRoot.Path);
        var workspaceWatcher = new WorkspaceWatcher(_ => watcher, TimeSpan.FromMilliseconds(50));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;

        var watchTask = workspaceWatcher.Watch(
            repositoryRoot.Path,
            (changes, _) =>
            {
                callbackCount++;
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        watcher.RaiseCreated(Path.Combine(repositoryRoot.Path, "src", "Feature.cs"));
        watcher.RaiseChanged(Path.Combine(repositoryRoot.Path, "docs", "Guide.md"));

        var observedChanges = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        Assert.Equal(1, callbackCount);
        Assert.Collection(
            observedChanges,
            change =>
            {
                Assert.Equal(WorkspaceFileChangeAction.Added, change.ActionType);
                Assert.Equal(Path.Combine(repositoryRoot.Path, "src", "Feature.cs"), change.NewPath);
            },
            change =>
            {
                Assert.Equal(WorkspaceFileChangeAction.Modified, change.ActionType);
                Assert.Equal(Path.Combine(repositoryRoot.Path, "docs", "Guide.md"), change.NewPath);
            });
    }

    [Fact]
    public async Task WhenChangesOccurUnderIgnoredDirectories_ThenSkipsIgnoredPaths()
    {
        using var repositoryRoot = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watcher = new TestFileSystemWatcher(repositoryRoot.Path);
        var workspaceWatcher = new WorkspaceWatcher(_ => watcher, TimeSpan.FromMilliseconds(50));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watchTask = workspaceWatcher.Watch(
            repositoryRoot.Path,
            (changes, _) =>
            {
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        watcher.RaiseChanged(Path.Combine(repositoryRoot.Path, ".GIT", "Hook.md"));
        watcher.RaiseCreated(Path.Combine(repositoryRoot.Path, "src", "BIN", "Debug", "Generated.cs"));
        watcher.RaiseChanged(Path.Combine(repositoryRoot.Path, "src", "Obj", "Debug", "Generated.cs"));
        watcher.RaiseChanged(Path.Combine(repositoryRoot.Path, "src", ".VS", "workspace", "Generated.cs"));
        watcher.RaiseCreated(Path.Combine(repositoryRoot.Path, "src", ".IDEA", "workspace", "Guide.md"));
        watcher.RaiseChanged(Path.Combine(repositoryRoot.Path, "src", "NODE_MODULES", "pkg", "Generated.cs"));
        watcher.RaiseChanged(Path.Combine(repositoryRoot.Path, "src", "testresults", "Run1", "Guide.md"));
        watcher.RaiseChanged(Path.Combine(repositoryRoot.Path, "src", "Feature.cs"));

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        var change = Assert.Single(changes);
        Assert.Equal(WorkspaceFileChangeAction.Modified, change.ActionType);
        Assert.Equal(Path.Combine(repositoryRoot.Path, "src", "Feature.cs"), change.NewPath);
    }

    [Fact]
    public async Task WhenIgnoredDirectoryIsDeletedWithMixedCase_ThenDoesNotTriggerFatalRecovery()
    {
        using var repositoryRoot = new TemporaryDirectory();
        var ignoredDirectoryPath = Path.Combine(repositoryRoot.Path, "src", "TESTRESULTS", "Run1");
        Directory.CreateDirectory(ignoredDirectoryPath);
        File.WriteAllText(Path.Combine(ignoredDirectoryPath, "Guide.md"), "# Guide");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watcher = new TestFileSystemWatcher(repositoryRoot.Path);
        var workspaceWatcher = new WorkspaceWatcher(_ => watcher, TimeSpan.Zero);
        var callbackInvoked = false;
        var deletedDirectoryPath = Path.Combine(repositoryRoot.Path, "src", "TESTRESULTS");

        var watchTask = workspaceWatcher.Watch(
            repositoryRoot.Path,
            (_, _) =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            },
            cancellation.Token);

        Directory.Delete(deletedDirectoryPath, recursive: true);
        watcher.RaiseDeleted(deletedDirectoryPath);

        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        Assert.False(watchTask.IsCompleted);
        Assert.False(callbackInvoked);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => watchTask);
    }

    [Fact]
    public async Task WhenRelevantFileIsRenamed_ThenReportsRenamePaths()
    {
        using var repositoryRoot = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watcher = new TestFileSystemWatcher(repositoryRoot.Path);
        var workspaceWatcher = new WorkspaceWatcher(_ => watcher, TimeSpan.FromMilliseconds(50));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldPath = Path.Combine(repositoryRoot.Path, "src", "OldFeature.cs");
        var newPath = Path.Combine(repositoryRoot.Path, "src", "NewFeature.cs");

        var watchTask = workspaceWatcher.Watch(
            repositoryRoot.Path,
            (changes, _) =>
            {
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        watcher.RaiseRenamed(oldPath, newPath);

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        var change = Assert.Single(changes);
        Assert.Equal(WorkspaceFileChangeAction.Renamed, change.ActionType);
        Assert.Equal(oldPath, change.OldPath);
        Assert.Equal(newPath, change.NewPath);
    }

    [Fact]
    public async Task WhenRelevantDirectoryIsRenamed_ThenReportsContainedFileRenamePaths()
    {
        using var repositoryRoot = new TemporaryDirectory();
        var oldDirectoryPath = Path.Combine(repositoryRoot.Path, "src", "OldFeature");
        var newDirectoryPath = Path.Combine(repositoryRoot.Path, "src", "NewFeature");
        Directory.CreateDirectory(oldDirectoryPath);
        File.WriteAllText(Path.Combine(oldDirectoryPath, "Feature.cs"), "public sealed class Feature { }");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watcher = new TestFileSystemWatcher(repositoryRoot.Path);
        var workspaceWatcher = new WorkspaceWatcher(_ => watcher, TimeSpan.FromMilliseconds(50));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);

        var watchTask = workspaceWatcher.Watch(
            repositoryRoot.Path,
            (changes, _) =>
            {
                observedBatch.TrySetResult(changes);
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token);

        Directory.Move(oldDirectoryPath, newDirectoryPath);
        watcher.RaiseRenamed(oldDirectoryPath, newDirectoryPath);

        var changes = await observedBatch.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        await watchTask;

        var change = Assert.Single(changes);
        Assert.Equal(WorkspaceFileChangeAction.Renamed, change.ActionType);
        Assert.Equal(Path.Combine(oldDirectoryPath, "Feature.cs"), change.OldPath);
        Assert.Equal(Path.Combine(newDirectoryPath, "Feature.cs"), change.NewPath);
    }

    [Fact]
    public async Task WhenRelevantDirectoryIsDeleted_ThenThrows()
    {
        using var repositoryRoot = new TemporaryDirectory();
        var directoryPath = Path.Combine(repositoryRoot.Path, "src", "FeatureFolder");
        Directory.CreateDirectory(directoryPath);
        File.WriteAllText(Path.Combine(directoryPath, "Feature.cs"), "public sealed class Feature { }");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watcher = new TestFileSystemWatcher(repositoryRoot.Path);
        var workspaceWatcher = new WorkspaceWatcher(_ => watcher, TimeSpan.Zero);

        var watchTask = workspaceWatcher.Watch(
            repositoryRoot.Path,
            static (_, _) => Task.CompletedTask,
            cancellation.Token);

        Directory.Delete(directoryPath, recursive: true);
        watcher.RaiseDeleted(directoryPath);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => watchTask);
        Assert.Equal("Workspace watcher encountered a fatal file system watcher error.", exception.Message);
        Assert.Equal(
            "Workspace watcher detected a directory deletion that requires a full reindex.",
            exception.InnerException?.Message);
    }

    [Fact]
    public async Task WhenExtensionlessFileIsDeleted_ThenDoesNotTriggerFatalRecovery()
    {
        using var repositoryRoot = new TemporaryDirectory();
        var filePath = Path.Combine(repositoryRoot.Path, "LICENSE");
        File.WriteAllText(filePath, "license");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watcher = new TestFileSystemWatcher(repositoryRoot.Path);
        var workspaceWatcher = new WorkspaceWatcher(_ => watcher, TimeSpan.Zero);
        var callbackInvoked = false;

        var watchTask = workspaceWatcher.Watch(
            repositoryRoot.Path,
            (_, _) =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            },
            cancellation.Token);

        File.Delete(filePath);
        watcher.RaiseDeleted(filePath);

        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        Assert.False(watchTask.IsCompleted);
        Assert.False(callbackInvoked);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => watchTask);
    }

    [Fact]
    public async Task WhenFileSystemWatcherSignalsFatalError_ThenThrows()
    {
        using var repositoryRoot = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var watcher = new TestFileSystemWatcher(repositoryRoot.Path);
        var workspaceWatcher = new WorkspaceWatcher(_ => watcher, TimeSpan.Zero);
        var expectedException = new IOException("The watcher buffer overflowed.");

        var watchTask = workspaceWatcher.Watch(
            repositoryRoot.Path,
            static (_, _) => Task.CompletedTask,
            cancellation.Token);

        watcher.RaiseError(expectedException);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => watchTask);
        Assert.Equal("Workspace watcher encountered a fatal file system watcher error.", exception.Message);
        Assert.Same(expectedException, exception.InnerException);
    }

    private sealed class TestFileSystemWatcher(string repositoryRoot) : FileSystemWatcher(repositoryRoot)
    {
        public void RaiseChanged(string fullPath) => OnChanged(CreateChangedArgs(WatcherChangeTypes.Changed, fullPath));

        public void RaiseCreated(string fullPath) => OnCreated(CreateChangedArgs(WatcherChangeTypes.Created, fullPath));

        public void RaiseDeleted(string fullPath) => OnDeleted(CreateChangedArgs(WatcherChangeTypes.Deleted, fullPath));

        public void RaiseRenamed(string oldFullPath, string newFullPath)
        {
            var directoryPath = System.IO.Path.GetDirectoryName(newFullPath) ?? string.Empty;
            var oldName = System.IO.Path.GetFileName(oldFullPath);
            var newName = System.IO.Path.GetFileName(newFullPath);

            OnRenamed(new RenamedEventArgs(
                WatcherChangeTypes.Renamed,
                directoryPath,
                newName,
                oldName));
        }

        public void RaiseError(Exception exception) => OnError(new ErrorEventArgs(exception));

        private static FileSystemEventArgs CreateChangedArgs(
            WatcherChangeTypes changeType,
            string fullPath)
        {
            var directoryPath = System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty;
            var fileName = System.IO.Path.GetFileName(fullPath);

            return new FileSystemEventArgs(changeType, directoryPath, fileName);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SharpSense.WorkspaceWatcher.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
