using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharpSense.Cli.Ui.Api;

internal static class GraphEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapGraphEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/graph")
            .WithTags("Graph");

        group.MapGetGraphNodesPageEndpoint()
            .MapGetGraphEdgesPageEndpoint()
            .MapGetGraphNodeConnectionsEndpoint();

        return builder;
    }
}
