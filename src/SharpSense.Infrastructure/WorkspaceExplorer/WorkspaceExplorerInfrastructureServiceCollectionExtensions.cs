using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.WorkspaceExplorer.Abstractions;

namespace SharpSense.Infrastructure.WorkspaceExplorer;

public static class WorkspaceExplorerInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceExplorerInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IWorkspaceTreeRepository, WorkspaceTreeRepository>();

        services.TryAddScoped<IWorkspaceOverviewReader, WorkspaceOverviewReader>();

        return services;
    }
}
