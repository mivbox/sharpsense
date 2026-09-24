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
    [Theory]
    [InlineData("md")]
    [InlineData("markdown")]
    [InlineData("mdown")]
    [InlineData("mkd")]
    public async Task ExistingMarkdownChangedAndCreatedEventsRemainModificationsAfterReady(string extension)
    {
        var fileSystem = CreateRepositoryFileSystem();
        var name = $"guide.{extension}";
        fileSystem.AddFile($"/repo/{name}", new MockFileData("# Before"));
        var (watcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        IReadOnlyList<WorkspaceFileChange>? observed = null;
        var ready = false;
        var watching = watcher.Watch("/repo", (changes, _) =>
        {
            observed = changes;
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token, onReady: () => ready = true);

        Assert.True(ready);
        fileSystem.File.WriteAllText($"/repo/{name}", "# After");
        fileWatcher.Raise(value => value.Changed += null, new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo", name));
        fileWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo", name));
        await watching;

        Assert.NotNull(observed);
        Assert.Equal(2, observed.Count);
        Assert.All(observed, change => Assert.Equal(WorkspaceFileChangeAction.Modified, change.ActionType));
    }

    [Fact]
    public async Task NewMarkdownChangedBeforeCreatedAcrossBatchesStillStartsWithAdded()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var (watcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var firstBatch = new TaskCompletionSource<WorkspaceFileChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        WorkspaceFileChange? second = null;
        var watching = watcher.Watch("/repo", (changes, _) =>
        {
            var change = Assert.Single(changes);
            if (!firstBatch.TrySetResult(change))
            {
                second = change;
                cancellation.Cancel();
            }
            return Task.CompletedTask;
        }, cancellation.Token);

        fileSystem.AddFile("/repo/new.md", new MockFileData("# New generator input"));
        fileWatcher.Raise(value => value.Changed += null, new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo", "new.md"));
        Assert.Equal(WorkspaceFileChangeAction.Added, (await firstBatch.Task.WaitAsync(cancellation.Token)).ActionType);
        fileWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo", "new.md"));
        await watching;

        Assert.NotNull(second);
        Assert.Equal(WorkspaceFileChangeAction.Modified, second.ActionType);
    }

    [Fact]
    public async Task NewMarkdownCreatedAfterReadyRemainsAdded()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var (watcher, fileWatcher, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WorkspaceFileChange? observed = null;
        var watching = watcher.Watch("/repo", (changes, _) =>
        {
            observed = Assert.Single(changes);
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token);

        fileSystem.AddFile("/repo/docs/new.md", new MockFileData("# New"));
        directoryWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo", "docs"));
        fileWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo/docs", "new.md"));
        await watching;

        Assert.NotNull(observed);
        Assert.Equal(WorkspaceFileChangeAction.Added, observed.ActionType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreatedMarkdownStaysAddedThroughoutInitializationAndReconciliation(bool existedBeforeInitialization)
    {
        var fileSystem = CreateRepositoryFileSystem();
        if (existedBeforeInitialization) fileSystem.AddFile("/repo/input.md", new MockFileData("# Existing"));
        var (watcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ready = false;
        var actions = new List<WorkspaceFileChangeAction>();
        await watcher.Watch("/repo", (changes, _) =>
        {
            actions.Add(Assert.Single(changes).ActionType);
            if (!ready)
            {
                Assert.Equal(WorkspaceFileChangeAction.Added, actions[^1]);
                if (actions.Count == 1)
                {
                    fileWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo", "input.md"));
                }
            }
            else cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token, initialize: _ =>
        {
            // C# may already have evaluated its empty AdditionalFiles glob while
            // another language worker has yet to read this new Markdown input.
            fileSystem.AddFile("/repo/input.md", new MockFileData("# New input"));
            fileWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo", "input.md"));
            return Task.CompletedTask;
        }, onReady: () =>
        {
            Assert.Equal(2, actions.Count);
            ready = true;
            fileWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo", "input.md"));
        });

        Assert.Equal([WorkspaceFileChangeAction.Added, WorkspaceFileChangeAction.Added, WorkspaceFileChangeAction.Modified], actions);
    }

    [Fact]
    public async Task MarkdownDeletedBetweenInventoryAndSubscriptionIsNotKnownWhenRecreated()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/guide.md", new MockFileData("# Before"));
        var (watcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
        fileWatcher.SetupSet(value => value.EnableRaisingEvents = true)
            .Callback(() => fileSystem.File.Delete("/repo/guide.md"));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        WorkspaceFileChange? observed = null;
        await watcher.Watch("/repo", (changes, _) =>
        {
            observed = Assert.Single(changes);
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token, onReady: () =>
        {
            fileSystem.AddFile("/repo/guide.md", new MockFileData("# Recreated"));
            fileWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo", "guide.md"));
        });

        Assert.NotNull(observed);
        Assert.Equal(WorkspaceFileChangeAction.Added, observed.ActionType);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DeletedOrRenamedMarkdownMembershipIsForgottenBeforeRecreation(bool directory, bool rename)
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddFile("/repo/docs/guide.md", new MockFileData("# Original"));
        var (watcher, fileWatcher, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        IReadOnlyList<WorkspaceFileChange>? observed = null;
        var watching = watcher.Watch("/repo", (changes, _) =>
        {
            observed = changes;
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token);

        if (directory)
        {
            if (rename)
            {
                fileSystem.Directory.Move("/repo/docs", "/repo/moved");
                directoryWatcher.Raise(value => value.Renamed += null, new RenamedEventArgs(WatcherChangeTypes.Renamed, "/repo", "moved", "docs"));
            }
            else
            {
                fileSystem.Directory.Delete("/repo/docs", recursive: true);
                directoryWatcher.Raise(value => value.Deleted += null, new FileSystemEventArgs(WatcherChangeTypes.Deleted, "/repo", "docs"));
            }
        }
        else if (rename)
        {
            fileSystem.File.Move("/repo/docs/guide.md", "/repo/docs/moved.md");
            fileWatcher.Raise(value => value.Renamed += null, new RenamedEventArgs(WatcherChangeTypes.Renamed, "/repo/docs", "moved.md", "guide.md"));
        }
        else
        {
            fileSystem.File.Delete("/repo/docs/guide.md");
            fileWatcher.Raise(value => value.Deleted += null, new FileSystemEventArgs(WatcherChangeTypes.Deleted, "/repo/docs", "guide.md"));
        }

        fileSystem.AddFile("/repo/docs/guide.md", new MockFileData("# Recreated"));
        fileWatcher.Raise(value => value.Created += null, new FileSystemEventArgs(WatcherChangeTypes.Created, "/repo/docs", "guide.md"));
        await watching;

        Assert.NotNull(observed);
        Assert.Equal(WorkspaceFileChangeAction.Added, observed[^1].ActionType);
    }

    [Fact]
    public async Task ChangesDuringInitialScanAndReconciliationAreAppliedBeforeReady()
    {
        var fileSystem = CreateRepositoryFileSystem();
        const string path = "/repo/Feature.cs";
        fileSystem.AddFile(path, new MockFileData("initial"));
        var (watcher, fileWatcher, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishScan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var indexed = "previous index";
        var updates = 0;
        var ready = false;

        var watching = watcher.Watch("/repo", (changes, _) =>
        {
            Assert.False(ready);
            Assert.Equal(path, Assert.Single(changes).NewPath);
            indexed = fileSystem.File.ReadAllText(path);
            if (++updates == 1)
            {
                fileSystem.File.WriteAllText(path, "during reconciliation");
                fileWatcher.Raise(value => value.Changed += null,
                    new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo", "Feature.cs"));
            }
            return Task.CompletedTask;
        }, cancellation.Token, initialize: async token =>
        {
            Assert.True(fileWatcher.Object.EnableRaisingEvents);
            Assert.True(directoryWatcher.Object.EnableRaisingEvents);
            var scannedContent = fileSystem.File.ReadAllText(path);
            scanned.SetResult();
            await finishScan.Task.WaitAsync(token);
            indexed = scannedContent;
        }, onReady: () =>
        {
            Assert.Equal("during reconciliation", indexed);
            Assert.Equal(2, updates);
            ready = true;
            cancellation.Cancel();
        });

        await scanned.Task.WaitAsync(cancellation.Token);
        fileSystem.File.WriteAllText(path, "during initial scan");
        fileWatcher.Raise(value => value.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo", "Feature.cs"));
        finishScan.SetResult();
        await watching;

        Assert.True(ready);
        fileWatcher.Verify(value => value.Dispose(), Times.Once);
        directoryWatcher.Verify(value => value.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledInitializationDisposesWatchersWithoutApplyingBufferedEdits(bool cancel)
    {
        var fileSystem = CreateRepositoryFileSystem();
        var (watcher, fileWatcher, directoryWatcher) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ready = false;
        var updates = 0;
        var watching = watcher.Watch("/repo", (_, _) =>
        {
            updates++;
            return Task.CompletedTask;
        }, cancellation.Token, initialize: token =>
        {
            Assert.True(fileWatcher.Object.EnableRaisingEvents);
            fileWatcher.Raise(value => value.Changed += null,
                new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo", "Feature.cs"));
            if (cancel)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            throw new InvalidOperationException("Initial extraction failed before persistence.");
        }, onReady: () => ready = true);

        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watching);
        else await Assert.ThrowsAsync<InvalidOperationException>(() => watching);
        Assert.False(ready);
        Assert.Equal(0, updates);
        fileWatcher.Verify(value => value.Dispose(), Times.Once);
        directoryWatcher.Verify(value => value.Dispose(), Times.Once);
    }

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
        fileWatcher.Object.Filters.Should().Equal("*.cs", "*.csproj", "*.sln", "*.slnx", "*.props", "*.targets", "global.json", ".editorconfig", "*.ts", "*.tsx", "*.json", "*.md", "*.markdown", "*.mdown", "*.mkd", "*.yaml", "*.yml");
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

    [Theory]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    [InlineData("src/App.csproj")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Packages.props")]
    [InlineData("build/Shared.props")]
    [InlineData("build/Custom.targets")]
    [InlineData("global.json")]
    [InlineData(".editorconfig")]
    [InlineData("src/Component.tsx")]
    [InlineData("src/client.ts")]
    [InlineData("tsconfig.app.json")]
    [InlineData("configs/base.json")]
    [InlineData("packages/client/package.json")]
    public async Task WhenSourceOrBuildConfigurationChanges_ThenEmitsRelevantBatch(string relativePath)
    {
        var fileSystem = CreateRepositoryFileSystem();
        var path = "/repo/" + relativePath;
        fileSystem.AddFile(path, new MockFileData(""));
        var (workspaceWatcher, fileWatcher, _) = CreateWorkspaceWatcher(fileSystem);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var observedBatch = new TaskCompletionSource<IReadOnlyList<WorkspaceFileChange>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watchTask = workspaceWatcher.Watch("/repo", (changes, _) =>
        {
            observedBatch.TrySetResult(changes);
            cancellation.Cancel();
            return Task.CompletedTask;
        }, cancellation.Token);

        fileWatcher.Raise(candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(path)!, Path.GetFileName(path)));
        fileWatcher.Raise(candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/node_modules/package", "index.ts"));
        fileWatcher.Raise(candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/dist", "index.ts"));
        fileWatcher.Raise(candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/node_modules/package", "base.json"));
        fileWatcher.Raise(candidate => candidate.Changed += null,
            new FileSystemEventArgs(WatcherChangeTypes.Changed, "/repo/dist", "base.json"));

        var batch = await observedBatch.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await watchTask;
        Assert.Equal(path, Assert.Single(batch).NewPath);
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
