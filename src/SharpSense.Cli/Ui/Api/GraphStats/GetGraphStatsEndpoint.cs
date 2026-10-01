using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.GraphStats.GetGraphStats.Models;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GetGraphStatsEndpoint
{
    public static IEndpointRouteBuilder MapGetGraphStatsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/graph-stats", GetGraphStats)
            .WithName(nameof(GetGraphStats))
            .WithTags("Tools")
            .Produces<GraphStatsSnapshot>();

        return builder;
    }

    private static async Task<GraphStatsSnapshot> GetGraphStats(
        IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot> handler,
        CancellationToken ct) => await handler.Handle(
            new GetGraphStatsQuery(),
            ct);
}
