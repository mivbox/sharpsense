using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Indexing;

/// <summary>
/// Owns one cancellable indexing/watch session per workspace for the lifetime of the UI host.
/// Jobs never depend on a browser request or its mutable current workspace selection.
/// </summary>
public sealed partial class WorkspaceIndexingCoordinator : IHostedService, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Job> _jobs = [];
    private readonly Func<WorkspaceSelection, StartWorkspaceIndexingRequest, Action<WorkspaceIndexingUpdate>, CancellationToken, Task> _run;
    private readonly CancellationToken _applicationStopping;
    private readonly ILogger<WorkspaceIndexingCoordinator> _logger;
    private bool _stopping;

    public WorkspaceIndexingCoordinator(
        IServiceScopeFactory scopeFactory,
        WorkspaceCatalog catalog,
        IHostApplicationLifetime lifetime,
        ILogger<WorkspaceIndexingCoordinator> logger)
        : this((selection, request, update, ct) => WorkspaceIndexingSession.Run(scopeFactory, catalog, selection, request, update, ct),
            lifetime.ApplicationStopping, logger)
    {
    }

    internal WorkspaceIndexingCoordinator(
        Func<WorkspaceSelection, StartWorkspaceIndexingRequest, Action<WorkspaceIndexingUpdate>, CancellationToken, Task> run,
        CancellationToken applicationStopping,
        ILogger<WorkspaceIndexingCoordinator> logger)
    {
        _run = run;
        _applicationStopping = applicationStopping;
        _logger = logger;
    }

    public WorkspaceIndexingStatus GetStatus(Guid workspaceId)
    {
        lock (_gate)
        {
            return _jobs.TryGetValue(workspaceId, out var job)
                ? job.Status
                : new WorkspaceIndexingStatus(workspaceId, null, "idle", false, null, null, null, null, null, [], 0, StreamId: _streamId, UpdatedAt: _streamStartedAt);
        }
    }

    public WorkspaceIndexingStatus Start(WorkspaceSelection selection, StartWorkspaceIndexingRequest request)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return Start(() => selection, request);
    }

    public WorkspaceIndexingStatus Start(Func<WorkspaceSelection> resolveSelection, StartWorkspaceIndexingRequest request)
    {
        ArgumentNullException.ThrowIfNull(resolveSelection);
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (_stopping || _applicationStopping.IsCancellationRequested)
            {
                throw new InvalidOperationException("UI host is stopping and cannot start another indexing job.");
            }

            // Resolve under the same gate used for definition edits. A job always captures
            // the latest complete definition before it becomes active.
            var selection = resolveSelection();
            var workspaceId = selection.Definition.Id;
            if (_jobs.TryGetValue(workspaceId, out var existing) && IsActive(existing.Status.State))
            {
                return existing.Status;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
            var job = new Job(cancellation, new WorkspaceIndexingStatus(
                workspaceId, Guid.NewGuid(), "indexing", request.Watch, DateTimeOffset.UtcNow, null,
                "Preparing workspace index...", null, null, [], existing?.Status.Revision ?? 0, existing?.Status.Sequence ?? 0));
            _jobs[workspaceId] = job;
            Publish(job);
            job.Task = Task.Run(() => Execute(job, selection, request), CancellationToken.None);
            return job.Status;
        }
    }

    public T UpdateWhileIdle<T>(Guid workspaceId, Func<T> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_gate)
        {
            if (_jobs.TryGetValue(workspaceId, out var job) && IsActive(job.Status.State))
            {
                throw new WorkspaceBusyException("Stop workspace indexing or watching before changing its sources.");
            }

            return update();
        }
    }

    public async Task<WorkspaceIndexingStatus> Stop(Guid workspaceId, CancellationToken ct)
    {
        Task? task;
        lock (_gate)
        {
            if (!_jobs.TryGetValue(workspaceId, out var job) || !IsActive(job.Status.State))
            {
                return GetStatus(workspaceId);
            }

            job.Status = job.Status with { State = "stopping", Message = "Stopping workspace indexing..." };
            Publish(job);
            job.Cancellation.Cancel();
            task = job.Task;
        }

        if (task is not null)
        {
            await task.WaitAsync(ct);
        }

        return GetStatus(workspaceId);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task[] tasks;
        lock (_gate)
        {
            _stopping = true;
            foreach (var job in _jobs.Values.Where(static job => IsActive(job.Status.State)))
            {
                job.Status = job.Status with { State = "stopping", Message = "UI host is stopping..." };
                Publish(job);
                job.Cancellation.Cancel();
            }

            tasks = [.. _jobs.Values.Select(static job => job.Task).OfType<Task>()];
        }

        try
        {
            await Task.WhenAll(tasks).WaitAsync(cancellationToken);
        }
        finally
        {
            CompleteSubscriptions();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    private async Task Execute(Job job, WorkspaceSelection selection, StartWorkspaceIndexingRequest request)
    {
        try
        {
            await _run(selection, request, update => Update(job, update), job.Cancellation.Token);
            job.Cancellation.Token.ThrowIfCancellationRequested();
            Complete(job, "completed", "Workspace indexing complete.", []);
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            Complete(job, "stopped", "Workspace indexing stopped.", []);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Indexing workspace {WorkspaceId} failed", selection.Definition.Id);
            Complete(job, "failed", "Workspace indexing failed.",
                [exception.Message]);
        }
        finally
        {
            job.Cancellation.Dispose();
        }
    }

    private void Update(Job job, WorkspaceIndexingUpdate update)
    {
        lock (_gate)
        {
            if (!IsActive(job.Status.State))
            {
                return;
            }

            if (job.Status.State == "stopping")
            {
                // Cancellation may arrive just after an atomic graph commit. Preserve its
                // revision even while suppressing late progress/state updates.
                if (update.IndexCommitted || update.Analysis is { State: not "running" })
                {
                    job.Status = job.Status with
                    {
                        Revision = job.Status.Revision + (update.IndexCommitted ? 1 : 0),
                        Analysis = update.Analysis ?? job.Status.Analysis,
                        Diagnostics = update.Diagnostics is null ? job.Status.Diagnostics : Bound(update.Diagnostics)
                    };
                    Publish(job);
                }

                return;
            }

            job.Status = job.Status with
            {
                State = update.State,
                Message = Limit(update.Message),
                CompletedItems = update.CompletedItems,
                TotalItems = update.TotalItems,
                Diagnostics = update.Diagnostics is null ? job.Status.Diagnostics : Bound(update.Diagnostics),
                Revision = job.Status.Revision + (update.IndexCommitted ? 1 : 0),
                Analysis = update.Analysis ?? job.Status.Analysis
            };
            Publish(job);
        }
    }

    private void Complete(Job job, string state, string message, IReadOnlyList<string> diagnostics)
    {
        lock (_gate)
        {
            job.Status = job.Status with
            {
                State = state,
                Message = message,
                CompletedAt = DateTimeOffset.UtcNow,
                Diagnostics = Bound(diagnostics.Count == 0 ? job.Status.Diagnostics : diagnostics)
            };
            Publish(job);
        }
    }

    private static bool IsActive(string state) => state is "indexing" or "watching" or "stopping";
    private static string Limit(string message) => message.Length <= 2000 ? message : message[..2000];
    private static string[] Bound(IReadOnlyList<string> diagnostics) => [.. diagnostics.Take(20).Select(Limit)];

    private sealed class Job(CancellationTokenSource cancellation, WorkspaceIndexingStatus status)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public WorkspaceIndexingStatus Status { get; set; } = status;
        public Task? Task { get; set; }
    }
}

public sealed class WorkspaceBusyException(string message) : InvalidOperationException(message);
