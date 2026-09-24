using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.GraphStats.GetGraphStats;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GraphStatsEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/tools/graph-stats", async (
                IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot> handler,
                CancellationToken ct) => await handler.Handle(new GetGraphStatsQuery(), ct))
            .WithName("GetGraphStats")
            .WithTags("Tools")
            .Produces<GraphStatsSnapshot>();
    }
}
