using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharpSense.Cli.Ui.Api;

internal static class WorkspaceEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api")
            .WithTags("Workspace");

        group.MapGetWorkspaceTreeEndpoint()
            .MapGetWorkspaceOverviewEndpoint();

        return builder;
    }
}
