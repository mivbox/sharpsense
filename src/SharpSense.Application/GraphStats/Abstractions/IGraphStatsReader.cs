using SharpSense.Application.GraphStats.Models;

namespace SharpSense.Application.GraphStats.Abstractions;

public interface IGraphStatsReader
{
    Task<GraphStatsSnapshot> Read(CancellationToken ct);
}
