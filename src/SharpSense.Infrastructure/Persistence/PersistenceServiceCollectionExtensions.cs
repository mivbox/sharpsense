using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    private static readonly ILogger _logger = Log.ForContext(typeof(PersistenceServiceCollectionExtensions));

    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFileSystem();
        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContextFactory<SharpSenseDbContext>(
            (serviceProvider, options) =>
            {
                var repositoryWorkspace = serviceProvider.GetRequiredService<IRepositoryWorkspace>();
                _logger.Information(
                    "Configuring SQLite database context for {DatabasePath}",
                    repositoryWorkspace.DatabasePath);

                options.UseSqlite(
                        $"Data Source={repositoryWorkspace.DatabasePath};Mode=ReadWriteCreate;Cache=Shared",
                        b => b.MigrationsAssembly(typeof(SharpSenseDbContext).Assembly.FullName))
                    .AddInterceptors(serviceProvider.GetRequiredService<SqlitePragmaInterceptor>());
            });

        services.AddHostedService<EfCoreEnsureDatabase>();

        return services;
    }

    internal class EfCoreEnsureDatabase(
        IServiceProvider serviceProvider,
        IFileSystem fileSystem) : IHostedService
    {
        public async Task StartAsync(CancellationToken ct)
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var repositoryWorkspace = scope.ServiceProvider.GetRequiredService<IRepositoryWorkspace>();

            var directory = fileSystem.Path.GetDirectoryName(repositoryWorkspace.DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory) && !fileSystem.Directory.Exists(directory))
            {
                fileSystem.Directory.CreateDirectory(directory);
            }

            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>();
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
            await dbContext.Database.MigrateAsync(ct);
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
