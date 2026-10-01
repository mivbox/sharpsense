using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SharpSense.Cli.Ui.Indexing;

internal sealed partial class WorkspaceIndexingCoordinator
{
    private readonly Guid _streamId = Guid.NewGuid();
    private readonly DateTimeOffset _streamStartedAt = DateTimeOffset.UtcNow;
    private readonly Dictionary<Guid, HashSet<Channel<WorkspaceIndexingStatus>>> _subscribers = [];

    /// <summary>
    /// Every subscriber starts with a current snapshot. Slow readers retain only the latest
    /// status; notification delivery never waits inside an indexing worker.
    /// </summary>
    internal WorkspaceIndexingSubscription Subscribe(Guid workspaceId)
    {
        lock (_gate)
        {
            var channel = Channel.CreateBounded<WorkspaceIndexingStatus>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                AllowSynchronousContinuations = false
            });
            if (_stopping)
            {
                channel.Writer.TryComplete();

                return new WorkspaceIndexingSubscription(
                    channel.Reader,
                    () =>
                    {
                    });
            }

            if (!_subscribers.TryGetValue(workspaceId, out var channels))
            {
                channels = [];
                _subscribers.Add(workspaceId, channels);
            }

            channels.Add(channel);
            channel.Writer.TryWrite(GetStatus(workspaceId));

            return new WorkspaceIndexingSubscription(
                channel.Reader,
                () =>
                {
                    lock (_gate)
                    {
                        channels.Remove(channel);
                        if (channels.Count == 0)
                        {
                            _subscribers.Remove(workspaceId);
                        }
                        channel.Writer.TryComplete();
                    }
                });
        }
    }

    public async IAsyncEnumerable<SseItem<WorkspaceIndexingStatus>> Events(
        Guid workspaceId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var subscription = Subscribe(workspaceId);
        var reader = subscription.Reader;
        if (!await reader.WaitToReadAsync(ct) || !reader.TryRead(out var initial))
        {
            yield break;
        }

        yield return Event(initial);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var heartbeat = false;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    if (!await reader.WaitToReadAsync(timeout.Token))
                    {
                        yield break;
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    heartbeat = true;
                }
            }

            if (heartbeat)
            {
                yield return new SseItem<WorkspaceIndexingStatus>(GetStatus(workspaceId), "heartbeat");
                continue;
            }

            // Coalesce bursts to at most ten network updates/second, with constant queue space.
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            WorkspaceIndexingStatus? latest = null;
            while (reader.TryRead(out var status))
            {
                latest = status;
            }
            if (latest is not null)
            {
                yield return Event(latest);
            }
        }
    }

    private static SseItem<WorkspaceIndexingStatus> Event(WorkspaceIndexingStatus status)
        => new(status, "status")
        {
            EventId = $"{status.StreamId}:{status.Sequence}"
        };

    // Caller holds _gate. The sequence orders every status change, independently of graph commits.
    private void Publish(Job job)
    {
        job.Status = job.Status with
        {
            Sequence = job.Status.Sequence + 1,
            StreamId = _streamId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        if (_subscribers.TryGetValue(job.Status.WorkspaceId, out var channels))
        {
            foreach (var channel in channels)
            {
                channel.Writer.TryWrite(job.Status);
            }
        }
    }

    private void CompleteSubscriptions()
    {
        lock (_gate)
        {
            foreach (var channels in _subscribers.Values)
            {
                foreach (var channel in channels)
                {
                    channel.Writer.TryComplete();
                }
            }
            _subscribers.Clear();
        }
    }
}
