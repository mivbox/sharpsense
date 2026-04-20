using SharpSense.Application.Features.Trace.Infrastructure;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Trace;

public sealed class TraceQueryHandler(ITraceService traceService)
    : IQueryHandler<TraceQuery, CodeNodeResult[]>
{
    public Task<CodeNodeResult[]> Handle(TraceQuery query, CancellationToken ct)
        => traceService.GetCallees(query, ct);
}
