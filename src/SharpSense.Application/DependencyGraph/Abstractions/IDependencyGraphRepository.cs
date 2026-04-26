using SharpSense.Application.DependencyGraph.Models;

namespace SharpSense.Application.DependencyGraph.Abstractions;

public interface IDependencyGraphRepository
{
    Task<GraphResult> GetGraph(CancellationToken ct);
}
