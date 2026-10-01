using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace SharpSense.Cli.Ui.Api;

internal static class ToolsEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapToolsEndpoints(this IEndpointRouteBuilder builder)
    {
        builder.MapGetToolsEndpoint();

        var group = builder.MapGroup("/api/tools");

        group.MapGetGraphStatsEndpoint()
            .MapSearchWorkspaceEndpoint()
            .MapGetNodeContextEndpoint()
            .MapTraceNodeEndpoint()
            .MapGetImpactEndpoint()
            .MapGetInheritorsEndpoint();

        return builder;
    }
}
