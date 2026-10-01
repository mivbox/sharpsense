using System.Threading.Channels;

namespace SharpSense.Cli.Ui.Indexing;

internal sealed class WorkspaceIndexingSubscription(
    ChannelReader<WorkspaceIndexingStatus> reader,
    Action dispose) : IDisposable
{
    private Action? _dispose = dispose;
    public ChannelReader<WorkspaceIndexingStatus> Reader { get; } = reader;
    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
