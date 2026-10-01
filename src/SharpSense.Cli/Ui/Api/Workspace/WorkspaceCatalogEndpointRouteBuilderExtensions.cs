using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharpSense.Cli.Ui.Api;

internal static class WorkspaceCatalogEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceCatalogEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/workspaces")
            .WithTags("Workspaces");

        group.MapListWorkspacesEndpoint()
            .MapGetWorkspaceEndpoint()
            .MapCreateWorkspaceEndpoint()
            .MapUpdateWorkspaceEndpoint()
            .MapMergeWorkspacesEndpoint()
            .MapDiscoverWorkspaceSourcesEndpoint();

        return builder;
    }
}
