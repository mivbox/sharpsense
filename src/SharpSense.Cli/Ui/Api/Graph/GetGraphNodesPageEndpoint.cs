using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.DependencyGraph.GetGraphPages.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GetGraphNodesPageEndpoint
{
    public static IEndpointRouteBuilder MapGetGraphNodesPageEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/nodes/page", GetGraphNodesPage)
            .WithName(nameof(GetGraphNodesPage))
            .Produces<GraphNodesPage>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        return builder;
    }

    private static async Task<GraphNodesPage> GetGraphNodesPage(
        int[]? directoryIds,
        string? cursor,
        string? revision,
        int? pageSize,
        bool? includeTotal,
        IQueryHandler<GetGraphNodesPageQuery, GraphNodesPage> handler,
        CancellationToken ct) => await handler.Handle(
            new GetGraphNodesPageQuery(new GraphPageRequest(
                directoryIds ?? [],
                pageSize ?? 2_000,
                cursor,
                revision,
                includeTotal ?? false)),
            ct);
}
