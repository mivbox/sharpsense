using System.Collections.Concurrent;
using JetBrains.Annotations;
using Serilog;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Indexing.Markdown;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Indexing.Watching;

[PublicAPI]
public sealed class WorkspaceWatcher : IWorkspaceWatcher
{
    private static readonly ILogger _logger = Log.ForContext<WorkspaceWatcher>();
    private readonly IFileSystem _fileSystem;
    private readonly IFileSystemWatcherFactory _watcherFactory;
    private readonly TimeSpan _debounceDelay;

    public WorkspaceWatcher(
        IFileSystem fileSystem,
        IFileSystemWatcherFactory watcherFactory)
        : this(fileSystem, watcherFactory, TimeSpan.FromMilliseconds(250))
    {
    }

    internal WorkspaceWatcher(
        IFileSystem fileSystem,
        IFileSystemWatcherFactory watcherFactory,
        TimeSpan debounceDelay)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _watcherFactory = watcherFactory ?? throw new ArgumentNullException(nameof(watcherFactory));

        if (debounceDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(debounceDelay));
        }

        _debounceDelay = debounceDelay;
    }

    public async Task Watch(
        string repositoryRoot,
        Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> onBatchChanged,
        CancellationToken ct,
        Func<CancellationToken, Task>? initialize = null,
        Action? onReady = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(onBatchChanged);

        var normalizedRepositoryRoot = _fileSystem.Path.TrimEndingDirectorySeparator(
            _fileSystem.Path.GetFullPath(repositoryRoot));
        using var signal = new SemaphoreSlim(0, int.MaxValue);
        var watchers = CreateWatchers(normalizedRepositoryRoot);
        using var fileWatcher = watchers.FileWatcher;
        using var directoryWatcher = watchers.DirectoryWatcher;
        var knownDirectories = CreateKnownDirectories(normalizedRepositoryRoot);
        var knownMarkdownPaths = CreateKnownMarkdownPaths(normalizedRepositoryRoot, ct);
        var stateGate = new object();
        var ready = false;
        var pendingChanges = new ConcurrentQueue<WorkspaceFileChange>();
        var pendingChangeCount = 0;
        Exception? fatalWatchException = null;

        fileWatcher.Changed += (_, args) => HandleChanged(args.FullPath);
        fileWatcher.Created += (_, args) => HandleCreated(args.FullPath);
        fileWatcher.Deleted += (_, args) => HandleDeleted(args.FullPath);
        fileWatcher.Renamed += (_, args) => HandleRenamed(args.OldFullPath, args.FullPath);
        fileWatcher.Error += (_, args) => RequestFatalError(args.GetException());
        directoryWatcher.Changed += (_, args) => HandleChanged(args.FullPath);
        directoryWatcher.Created += (_, args) => HandleCreated(args.FullPath);
        directoryWatcher.Deleted += (_, args) => HandleDeleted(args.FullPath);
        directoryWatcher.Renamed += (_, args) => HandleRenamed(args.OldFullPath, args.FullPath);
        directoryWatcher.Error += (_, args) => RequestFatalError(args.GetException());
        fileWatcher.EnableRaisingEvents = true;
        directoryWatcher.EnableRaisingEvents = true;

        // Initial scans can take minutes. Keep subscriptions active throughout the
        // scan and reconcile its queued edits before advertising a current index.
        if (initialize is not null)
        {
            await initialize(ct);
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref fatalWatchException) is { } startupException)
            {
                throw CreateFatalWatchException(startupException);
            }

            IReadOnlyList<WorkspaceFileChange> bufferedChanges;
            lock (stateGate)
            {
                Interlocked.Exchange(ref pendingChangeCount, 0);
                bufferedChanges = DrainChanges(pendingChanges);
                if (bufferedChanges.Count == 0)
                {
                    // A file can disappear between inventory and subscription. Never
                    // retain that stale baseline when classifying its future creation.
                    knownMarkdownPaths.RemoveWhere(path => !_fileSystem.File.Exists(path));
                    ready = true;
                }
            }
            if (bufferedChanges.Count == 0)
            {
                break;
            }

            await onBatchChanged(bufferedChanges, ct);
        }

        onReady?.Invoke();

        while (!ct.IsCancellationRequested)
        {
            await signal.WaitAsync(ct);

            if (Volatile.Read(ref fatalWatchException) is { } watchException)
            {
                throw CreateFatalWatchException(watchException);
            }

            await Task.Delay(_debounceDelay, ct);

            if (Volatile.Read(ref fatalWatchException) is { } delayedWatchException)
            {
                throw CreateFatalWatchException(delayedWatchException);
            }

            Interlocked.Exchange(ref pendingChangeCount, 0);
            var changedFiles = DrainChanges(pendingChanges);
            if (changedFiles.Count == 0)
            {
                continue;
            }

            if (Volatile.Read(ref fatalWatchException) is { } callbackWatchException)
            {
                throw CreateFatalWatchException(callbackWatchException);
            }

            await onBatchChanged(changedFiles, ct);
        }

        return;

        bool IsFastPathIgnored(string path)
        {
            var sep = _fileSystem.Path.DirectorySeparatorChar;
            return path.Contains($"{sep}.git{sep}", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith($"{sep}.git", StringComparison.OrdinalIgnoreCase);
        }

        void HandleChanged(string fullPath)
        {
            if (IsFastPathIgnored(fullPath) || _fileSystem.Directory.Exists(fullPath))
            {
                return;
            }

            lock (stateGate)
            {
                EnqueueChange(new WorkspaceFileChange(
                    ClassifyMarkdownChange(fullPath, WorkspaceFileChangeAction.Modified),
                    NewPath: fullPath));
            }
        }

        void HandleCreated(string fullPath)
        {
            if (IsFastPathIgnored(fullPath))
            {
                return;
            }

            if (_fileSystem.Directory.Exists(fullPath))
            {
                AddKnownDirectoryTree(knownDirectories, fullPath);
                return;
            }

            lock (stateGate)
            {
                EnqueueChange(new WorkspaceFileChange(
                    ClassifyMarkdownChange(fullPath, WorkspaceFileChangeAction.Added), NewPath: fullPath));
            }
        }

        WorkspaceFileChangeAction ClassifyMarkdownChange(string fullPath, WorkspaceFileChangeAction action)
        {
            if (!MarkdownIndexer.IsMarkdownDocumentPath(fullPath) || !IsRelevantPath(normalizedRepositoryRoot, fullPath))
            {
                return action;
            }

            var path = NormalizeDirectoryPath(fullPath);
            if (!_fileSystem.File.Exists(path))
            {
                knownMarkdownPaths.Remove(path);
                return WorkspaceFileChangeAction.Added;
            }

            var known = !knownMarkdownPaths.Add(path);
            // Some platforms deliver Changed before Created, possibly in another batch.
            // The first observation of new membership must always trigger a full refresh.
            if (!known)
            {
                return WorkspaceFileChangeAction.Added;
            }

            // macOS can report Created as well as Changed for an existing document write.
            // Initial workers still need raw Added events because they can observe
            // different file membership; normalization starts after reconciliation.
            return ready ? WorkspaceFileChangeAction.Modified : action;
        }

        void HandleDeleted(string fullPath)
        {
            if (IsFastPathIgnored(fullPath))
            {
                return;
            }

            lock (stateGate)
            {
                ForgetMarkdownPaths(fullPath);
                if (RemoveKnownDirectoryTree(knownDirectories, fullPath))
                {
                    EnqueueChange(new WorkspaceFileChange(
                        WorkspaceFileChangeAction.DirectoryDeleted,
                        OldPath: fullPath));
                }
                else
                {
                    EnqueueChange(new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Deleted,
                        OldPath: fullPath));
                }
            }
        }

        void HandleRenamed(
            string oldFullPath,
            string newFullPath)
        {
            var oldFastPathIgnored = IsFastPathIgnored(oldFullPath);
            var newFastPathIgnored = IsFastPathIgnored(newFullPath);

            if (oldFastPathIgnored && newFastPathIgnored)
            {
                return;
            }

            lock (stateGate)
            {
                ForgetMarkdownPaths(oldFullPath);
                ForgetMarkdownPaths(newFullPath);
                if (RemoveKnownDirectoryTree(knownDirectories, oldFullPath) ||
                    _fileSystem.Directory.Exists(newFullPath))
                {
                    AddKnownDirectoryTree(knownDirectories, newFullPath);
                    EnqueueChange(new WorkspaceFileChange(
                        WorkspaceFileChangeAction.DirectoryRenamed,
                        oldFullPath,
                        newFullPath));
                    return;
                }

                EnqueueChanges(CreateRenameChanges(
                    normalizedRepositoryRoot,
                    oldFullPath,
                    newFullPath));
            }
        }

        void ForgetMarkdownPaths(string path)
        {
            var normalized = NormalizeDirectoryPath(path);
            knownMarkdownPaths.RemoveWhere(known => IsSameOrSubPath(known, normalized));
        }

        void EnqueueChange(WorkspaceFileChange changedFile)
        {
            if (!IsRelevantChange(normalizedRepositoryRoot, changedFile))
            {
                return;
            }

            lock (stateGate)
            {
                pendingChanges.Enqueue(changedFile);
                if (Interlocked.Increment(ref pendingChangeCount) == 1)
                {
                    ReleaseSignal();
                }
            }
        }

        void EnqueueChanges(IEnumerable<WorkspaceFileChange> changedFiles)
        {
            foreach (var changedFile in changedFiles)
            {
                EnqueueChange(changedFile);
            }
        }

        void RequestFatalError(Exception? exception)
        {
            var watchException = exception ?? new InvalidOperationException(
                "The file system watcher reported an unknown fatal error.");

            if (Interlocked.CompareExchange(ref fatalWatchException, watchException, null) is not null)
            {
                return;
            }

            _logger.Warning(watchException, "Workspace watcher encountered a fatal file system watcher error.");
            ReleaseSignal();
        }

        void ReleaseSignal()
        {
            try
            {
                signal.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private (IFileSystemWatcher FileWatcher, IFileSystemWatcher DirectoryWatcher) CreateWatchers(string repositoryRoot)
    {
        var fileWatcher = _watcherFactory.New(repositoryRoot);
        fileWatcher.IncludeSubdirectories = true;
        fileWatcher.InternalBufferSize = 64 * 1024;
        fileWatcher.NotifyFilter = NotifyFilters.CreationTime |
                                   NotifyFilters.FileName |
                                   NotifyFilters.LastWrite |
                                   NotifyFilters.Size;
        fileWatcher.Filters.Add("*.cs");
        fileWatcher.Filters.Add("*.csproj");
        fileWatcher.Filters.Add("*.sln");
        fileWatcher.Filters.Add("*.slnx");
        fileWatcher.Filters.Add("*.props");
        fileWatcher.Filters.Add("*.targets");
        fileWatcher.Filters.Add("global.json");
        fileWatcher.Filters.Add(".editorconfig");
        fileWatcher.Filters.Add("*.ts");
        fileWatcher.Filters.Add("*.tsx");
        // TypeScript extends/references can point to configs such as base.json.
        fileWatcher.Filters.Add("*.json");
        fileWatcher.Filters.Add("*.md");
        fileWatcher.Filters.Add("*.markdown");
        fileWatcher.Filters.Add("*.mdown");
        fileWatcher.Filters.Add("*.mkd");
        fileWatcher.Filters.Add("*.yaml");
        fileWatcher.Filters.Add("*.yml");

        var directoryWatcher = _watcherFactory.New(repositoryRoot);
        directoryWatcher.IncludeSubdirectories = true;
        directoryWatcher.InternalBufferSize = 16 * 1024;
        directoryWatcher.NotifyFilter = NotifyFilters.DirectoryName;

        return (fileWatcher, directoryWatcher);
    }

    private static InvalidOperationException CreateFatalWatchException(Exception exception)
        => new("Workspace watcher encountered a fatal file system watcher error.", exception);

    private static IReadOnlyList<WorkspaceFileChange> DrainChanges(ConcurrentQueue<WorkspaceFileChange> pendingChanges)
    {
        var changedFiles = new List<WorkspaceFileChange>();

        while (pendingChanges.TryDequeue(out var changedFile))
        {
            changedFiles.Add(changedFile);
        }

        return changedFiles;
    }

    private bool IsRelevantChange(
        string repositoryRoot,
        WorkspaceFileChange changedFile)
        => changedFile.ActionType switch
        {
            WorkspaceFileChangeAction.DirectoryDeleted or WorkspaceFileChangeAction.DirectoryRenamed
                => changedFile.GetAffectedPaths().Any(path => IsRelevantDirectoryPath(repositoryRoot, path)),
            _ => changedFile.GetAffectedPaths().Any(path => IsRelevantPath(repositoryRoot, path))
        };

    private bool IsRelevantPath(
        string repositoryRoot,
        string path)
    {
        if (!TryGetRepositoryRelativePath(repositoryRoot, path, out var relativePath))
        {
            return false;
        }

        if (IsIgnoredPath(relativePath))
        {
            return false;
        }

        return IsTargetPath(relativePath);
    }

    private static bool IsIgnoredPath(string relativePath)
        => WorkspaceIndexingPathRules.IsIgnoredPath(relativePath);

    private bool IsRelevantDirectoryPath(
        string repositoryRoot,
        string path)
    {
        if (!TryGetRepositoryRelativePath(repositoryRoot, path, out var relativePath) ||
            IsIgnoredPath(relativePath))
        {
            return false;
        }

        return true;
    }

    private static bool IsTargetPath(string path)
    {
        return CSharpIndexingPathRules.IsRelevantChangePath(path) ||
               MarkdownIndexer.IsMarkdownDocumentPath(path) ||
               TypeScriptIndexingPathRules.IsRelevantChangePath(path);
    }

    private IEnumerable<WorkspaceFileChange> CreateRenameChanges(
        string repositoryRoot,
        string oldPath,
        string newPath)
    {
        var oldRelevant = IsRelevantPath(repositoryRoot, oldPath);
        var newRelevant = IsRelevantPath(repositoryRoot, newPath);

        if (oldRelevant && newRelevant)
        {
            yield return new WorkspaceFileChange(
                WorkspaceFileChangeAction.Renamed,
                oldPath,
                newPath);
            yield break;
        }

        if (oldRelevant)
        {
            yield return new WorkspaceFileChange(
                WorkspaceFileChangeAction.Deleted,
                OldPath: oldPath);
        }

        if (newRelevant)
        {
            yield return new WorkspaceFileChange(
                WorkspaceFileChangeAction.Added,
                NewPath: newPath);
        }
    }

    private ConcurrentDictionary<string, byte> CreateKnownDirectories(string repositoryRoot)
    {
        var knownDirectories = new ConcurrentDictionary<string, byte>(GetPathComparer());
        AddKnownDirectoryTree(knownDirectories, repositoryRoot);
        return knownDirectories;
    }

    private HashSet<string> CreateKnownMarkdownPaths(string repositoryRoot, CancellationToken ct)
    {
        var paths = new HashSet<string>(GetPathComparer());
        var pending = new Stack<string>();
        pending.Push(repositoryRoot);
        while (pending.TryPop(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var file in _fileSystem.Directory.EnumerateFiles(directory))
            {
                ct.ThrowIfCancellationRequested();
                if (MarkdownIndexer.IsMarkdownDocumentPath(file) && IsRelevantPath(repositoryRoot, file) &&
                    (_fileSystem.FileInfo.New(file).Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    paths.Add(NormalizeDirectoryPath(file));
                }
            }

            foreach (var child in _fileSystem.Directory.EnumerateDirectories(directory))
            {
                ct.ThrowIfCancellationRequested();
                if (IsRelevantDirectoryPath(repositoryRoot, child) &&
                    (_fileSystem.DirectoryInfo.New(child).Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(child);
                }
            }
        }
        return paths;
    }

    private void AddKnownDirectoryTree(
        ConcurrentDictionary<string, byte> knownDirectories,
        string directoryPath)
    {
        if (!_fileSystem.Directory.Exists(directoryPath))
        {
            return;
        }

        knownDirectories.TryAdd(NormalizeDirectoryPath(directoryPath), 0);

        foreach (var childDirectory in _fileSystem.Directory.EnumerateDirectories(directoryPath, "*", SearchOption.AllDirectories))
        {
            knownDirectories.TryAdd(NormalizeDirectoryPath(childDirectory), 0);
        }
    }

    private bool RemoveKnownDirectoryTree(
        ConcurrentDictionary<string, byte> knownDirectories,
        string directoryPath)
    {
        var normalizedDirectoryPath = NormalizeDirectoryPath(directoryPath);
        var matchingDirectories = knownDirectories.Keys
            .Where(path => IsSameOrSubPath(path, normalizedDirectoryPath))
            .ToArray();

        foreach (var matchingDirectory in matchingDirectories)
        {
            knownDirectories.TryRemove(matchingDirectory, out _);
        }

        return matchingDirectories.Length > 0;
    }

    private static bool IsSameOrSubPath(
        string path,
        string rootPath)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPathWithSeparator = rootPath.EndsWith(Path.DirectorySeparatorChar) ||
                                    rootPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        return string.Equals(path, rootPath, comparison) ||
               path.StartsWith(rootPathWithSeparator, comparison);
    }

    private string NormalizeDirectoryPath(string path)
        => _fileSystem.Path.TrimEndingDirectorySeparator(_fileSystem.Path.GetFullPath(path));

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private bool TryGetRepositoryRelativePath(
        string repositoryRoot,
        string path,
        out string relativePath)
    {
        var absolutePath = _fileSystem.Path.IsPathRooted(path)
            ? _fileSystem.Path.GetFullPath(path)
            : _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(repositoryRoot, path));
        relativePath = _fileSystem.Path.GetRelativePath(repositoryRoot, absolutePath);

        if (_fileSystem.Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(relativePath, ".", comparison))
        {
            relativePath = string.Empty;
            return true;
        }

        if (relativePath.StartsWith(".." + Path.DirectorySeparatorChar, comparison) ||
            relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, comparison) ||
            string.Equals(relativePath, "..", comparison))
        {
            return false;
        }

        return true;
    }
}
