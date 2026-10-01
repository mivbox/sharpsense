using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.DependencyGraph.GetGraphPages.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GetGraphNodeConnectionsEndpoint
{
    public static IEndpointRouteBuilder MapGetGraphNodeConnectionsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/nodes/{nodeId:int}/connections", GetGraphNodeConnections)
            .WithName(nameof(GetGraphNodeConnections))
            .Produces<GraphNodeConnectionsPage>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        return builder;
    }

    private static async Task<GraphNodeConnectionsPage> GetGraphNodeConnections(
        int nodeId,
        string? cursor,
        string? revision,
        int? pageSize,
        bool? includeTotal,
        IQueryHandler<GetGraphNodeConnectionsQuery, GraphNodeConnectionsPage> handler,
        CancellationToken ct) => await handler.Handle(
            new GetGraphNodeConnectionsQuery(new GraphNodeConnectionsRequest(
                nodeId,
                pageSize ?? 100,
                cursor,
                revision,
                includeTotal ?? false)),
            ct);
}
