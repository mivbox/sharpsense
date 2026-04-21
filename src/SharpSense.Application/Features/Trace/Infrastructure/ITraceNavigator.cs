using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Trace.Infrastructure;

public interface ITraceNavigator
{
    Task<CodeNodeResult[]> GetCallees(TraceQuery query, CancellationToken ct);
}
