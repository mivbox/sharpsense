using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Storage;

/// <summary>
/// Holds exclusive ownership of a workspace's indexing plan across CLI and UI processes.
/// The lock file remains after disposal so another process cannot lock a different inode.
/// </summary>
internal sealed class WorkspaceIndexLease : IDisposable
{
    private Stream? _handle;

    private WorkspaceIndexLease(Stream handle) => _handle = handle;

    internal static WorkspaceIndexLease Acquire(IFileSystem fileSystem, string directoryPath, string workspaceName)
    {
        var lockPath = fileSystem.Path.Combine(directoryPath, ".index.lock");
        try
        {
            return new WorkspaceIndexLease(fileSystem.FileStream.New(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None));
        }
        catch (IOException exception)
        {
            var busy = new WorkspaceIndexBusyException(
                $"Workspace '{workspaceName}' is already being indexed or watched by another command or UI session. Stop that session before indexing or changing workspace sources.");
            busy.Data["LockPath"] = lockPath;
            busy.Data["LockError"] = exception.Message;
            throw busy;
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _handle, null)?.Dispose();
}
