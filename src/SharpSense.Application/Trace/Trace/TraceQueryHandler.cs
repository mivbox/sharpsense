using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.Trace.Models;

namespace SharpSense.Application.Trace.Trace;

internal sealed class TraceQueryHandler(ITraceNavigator traceNavigator)
    : IQueryHandler<TraceQuery, CodeNodeResult[]>
{
    public Task<CodeNodeResult[]> Handle(TraceQuery query, CancellationToken ct)
        => traceNavigator.GetCallees(query, ct);
}
