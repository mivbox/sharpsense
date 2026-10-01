using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.DependencyGraph.GetGraphPages.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GetGraphEdgesPageEndpoint
{
    public static IEndpointRouteBuilder MapGetGraphEdgesPageEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/edges/page", GetGraphEdgesPage)
            .WithName(nameof(GetGraphEdgesPage))
            .Produces<GraphEdgesPage>()
            .ProducesProblem(400)
            .ProducesProblem(409);

        return builder;
    }

    private static async Task<GraphEdgesPage> GetGraphEdgesPage(
        int[]? directoryIds,
        string? cursor,
        string? revision,
        int? pageSize,
        bool? includeTotal,
        IQueryHandler<GetGraphEdgesPageQuery, GraphEdgesPage> handler,
        CancellationToken ct) => await handler.Handle(
            new GetGraphEdgesPageQuery(new GraphPageRequest(
                directoryIds ?? [],
                pageSize ?? 2_000,
                cursor,
                revision,
                includeTotal ?? false)),
            ct);
}
