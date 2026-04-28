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
    private static readonly HashSet<string> _ignoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        ".vs",
        ".idea",
        "node_modules",
        "TestResults"
    };

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
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(onBatchChanged);

        var normalizedRepositoryRoot = _fileSystem.Path.TrimEndingDirectorySeparator(
            _fileSystem.Path.GetFullPath(repositoryRoot));
        using var signal = new SemaphoreSlim(0, int.MaxValue);
        using var watcher = CreateWatcher(normalizedRepositoryRoot);
        var knownDirectories = CreateKnownDirectories(normalizedRepositoryRoot);
        var pendingChanges = new ConcurrentQueue<WorkspaceFileChange>();
        var pendingChangeCount = 0;
        Exception? fatalWatchException = null;

        watcher.Changed += (_, args) => HandleChanged(args.FullPath);
        watcher.Created += (_, args) => HandleCreated(args.FullPath);
        watcher.Deleted += (_, args) => HandleDeleted(args.FullPath);
        watcher.Renamed += (_, args) => HandleRenamed(args.OldFullPath, args.FullPath);
        watcher.Error += (_, args) => RequestFatalError(args.GetException());
        watcher.EnableRaisingEvents = true;

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

        void HandleChanged(string fullPath)
        {
            if (_fileSystem.Directory.Exists(fullPath))
            {
                AddKnownDirectoryTree(knownDirectories, fullPath);
                return;
            }

            EnqueueChange(new WorkspaceFileChange(
                WorkspaceFileChangeAction.Modified,
                NewPath: fullPath));
        }

        void HandleCreated(string fullPath)
        {
            if (_fileSystem.Directory.Exists(fullPath))
            {
                AddKnownDirectoryTree(knownDirectories, fullPath);
                return;
            }

            EnqueueChange(new WorkspaceFileChange(
                WorkspaceFileChangeAction.Added,
                NewPath: fullPath));
        }

        void HandleDeleted(string fullPath)
        {
            if (RemoveKnownDirectoryTree(knownDirectories, fullPath))
            {
                if (IsRelevantDirectoryPath(normalizedRepositoryRoot, fullPath))
                {
                    RequestFatalError(new InvalidOperationException(
                        "Workspace watcher detected a directory deletion that requires a full reindex."));
                }

                return;
            }

            EnqueueChange(new WorkspaceFileChange(
                WorkspaceFileChangeAction.Deleted,
                OldPath: fullPath));
        }

        void HandleRenamed(
            string oldFullPath,
            string newFullPath)
        {
            var wasDirectory = RemoveKnownDirectoryTree(knownDirectories, oldFullPath);
            if (_fileSystem.Directory.Exists(newFullPath) || wasDirectory)
            {
                AddKnownDirectoryTree(knownDirectories, newFullPath);
                EnqueueChanges(CreateDirectoryRenameChanges(
                    normalizedRepositoryRoot,
                    oldFullPath,
                    newFullPath));
                return;
            }

            EnqueueChanges(CreateRenameChanges(
                normalizedRepositoryRoot,
                oldFullPath,
                newFullPath));
        }

        void EnqueueChange(WorkspaceFileChange changedFile)
        {
            if (!IsRelevantChange(normalizedRepositoryRoot, changedFile))
            {
                return;
            }

            pendingChanges.Enqueue(changedFile);

            if (Interlocked.Increment(ref pendingChangeCount) == 1)
            {
                ReleaseSignal();
            }
        }

        void EnqueueChanges(IEnumerable<WorkspaceFileChange> changedFiles)
        {
            var enqueuedChanges = 0;

            foreach (var changedFile in changedFiles)
            {
                if (!IsRelevantChange(normalizedRepositoryRoot, changedFile))
                {
                    continue;
                }

                pendingChanges.Enqueue(changedFile);
                enqueuedChanges++;
            }

            if (enqueuedChanges > 0 &&
                Interlocked.Add(ref pendingChangeCount, enqueuedChanges) == enqueuedChanges)
            {
                ReleaseSignal();
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

    private IFileSystemWatcher CreateWatcher(string repositoryRoot)
    {
        var watcher = _watcherFactory.New(repositoryRoot);
        watcher.IncludeSubdirectories = true;
        watcher.InternalBufferSize = 64 * 1024;
        watcher.NotifyFilter = NotifyFilters.CreationTime |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.FileName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size;
        return watcher;
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
        => changedFile.GetAffectedPaths().Any(path => IsRelevantPath(repositoryRoot, path));

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
    {
        var segments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(_ignoredDirectoryNames.Contains);
    }

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

    private IEnumerable<WorkspaceFileChange> CreateDirectoryRenameChanges(
        string repositoryRoot,
        string oldDirectoryPath,
        string newDirectoryPath)
    {
        foreach (var newFilePath in _fileSystem.Directory.EnumerateFiles(newDirectoryPath, "*", SearchOption.AllDirectories))
        {
            if (!IsTargetPath(newFilePath))
            {
                continue;
            }

            var relativeChildPath = _fileSystem.Path.GetRelativePath(newDirectoryPath, newFilePath);
            var oldFilePath = _fileSystem.Path.Combine(oldDirectoryPath, relativeChildPath);

            foreach (var changedFile in CreateRenameChanges(repositoryRoot, oldFilePath, newFilePath))
            {
                yield return changedFile;
            }
        }
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

    private static bool IsTargetPath(string path)
    {
        return string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase) ||
               MarkdownIndexer.IsMarkdownDocumentPath(path);
    }

    private ConcurrentDictionary<string, byte> CreateKnownDirectories(string repositoryRoot)
    {
        var knownDirectories = new ConcurrentDictionary<string, byte>(GetPathComparer());
        AddKnownDirectoryTree(knownDirectories, repositoryRoot);
        return knownDirectories;
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
