using SharpSense.Application.Features.DependencyGraph.Contracts;

namespace SharpSense.Application.Features.DependencyGraph.Infrastructure;

public interface IDependencyGraphRepository
{
    Task<GraphResult> GetGraph(CancellationToken ct);
}
