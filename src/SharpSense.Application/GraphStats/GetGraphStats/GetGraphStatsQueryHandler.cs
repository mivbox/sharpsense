using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.GraphStats.GetGraphStats.Models;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.GraphStats.GetGraphStats;

internal sealed class GetGraphStatsQueryHandler(IGraphStatsReader reader)
    : IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot>
{
    public Task<GraphStatsSnapshot> Handle(GetGraphStatsQuery query, CancellationToken ct)
        => reader.Read(ct);
}
