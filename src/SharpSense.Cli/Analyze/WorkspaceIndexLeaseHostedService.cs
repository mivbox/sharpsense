using Microsoft.Extensions.Hosting;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Analyze;

/// <summary>
/// Acquires the workspace writer lease before persistence startup. The lease covers
/// migration, initial indexing, and the complete watch session, including host failures.
/// </summary>
internal sealed class WorkspaceIndexLeaseHostedService(
    WorkspaceCatalog catalog,
    WorkspaceSelection selection) : IHostedService, IDisposable
{
    private IDisposable? _lease;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _lease = catalog.AcquireIndexLease(selection);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => Interlocked.Exchange(ref _lease, null)?.Dispose();
}
