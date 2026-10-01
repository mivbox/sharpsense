using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree.Models;
using SharpSense.Application.WorkspaceExplorer.Models;

namespace SharpSense.Application.WorkspaceExplorer;

public static class WorkspaceExplorerServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceExplorer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services
            .TryAddTransient<IQueryHandler<GetWorkspaceTreeQuery, WorkspaceTreeResult>, GetWorkspaceTreeQueryHandler>();

        return services;
    }
}
