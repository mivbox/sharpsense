using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Persistence;

internal sealed class WorkspaceDatabaseStartup(
    IServiceProvider serviceProvider,
    IFileSystem fileSystem) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var repositoryWorkspace = scope.ServiceProvider.GetRequiredService<IRepositoryWorkspace>();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>();
        await WorkspaceDatabaseMigrator.Initialize(repositoryWorkspace, dbContextFactory, fileSystem, ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
