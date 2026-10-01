using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    private static readonly ILogger _logger = Log.ForContext(typeof(PersistenceServiceCollectionExtensions));

    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        bool initializeOnStartup = true,
        ServiceLifetime factoryLifetime = ServiceLifetime.Singleton)
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
            },
            factoryLifetime);

        services.AddScoped<IWorkspaceDatabaseInitializer, WorkspaceDatabaseInitializer>();
        if (initializeOnStartup)
        {
            services.AddHostedService<WorkspaceDatabaseStartup>();
        }

        return services;
    }
}
