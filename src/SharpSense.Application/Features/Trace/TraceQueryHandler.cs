using SharpSense.Application.Features.Trace.Infrastructure;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Trace;

public sealed class TraceQueryHandler(ITraceNavigator traceNavigator)
    : IQueryHandler<TraceQuery, CodeNodeResult[]>
{
    public Task<CodeNodeResult[]> Handle(TraceQuery query, CancellationToken ct)
        => traceNavigator.GetCallees(query, ct);
}
