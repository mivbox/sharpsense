using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Trace.Models;

namespace SharpSense.Application.Trace.Abstractions;

public interface ITraceNavigator
{
    Task<CodeNodeResult?> GetRootNode(string identifier, CancellationToken ct);
    Task<CodeNodeResult[]> GetCallees(TraceQuery query, CancellationToken ct);
}
