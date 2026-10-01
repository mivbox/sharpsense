using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.GetTraceGraph;
using SharpSense.Application.Trace.GetTraceGraph.Models;
using SharpSense.Application.Trace.Trace;
using SharpSense.Application.Trace.Trace.Models;

namespace SharpSense.Application.Trace;

public static class TraceServiceCollectionExtensions
{
    public static IServiceCollection AddTrace(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<IQueryHandler<TraceQuery, CodeNodeResult[]>, TraceQueryHandler>();

        services
            .TryAddTransient<IQueryHandler<GetTraceGraphQuery, Result<TraceGraphResult>>, GetTraceGraphQueryHandler>();

        return services;
    }
}
