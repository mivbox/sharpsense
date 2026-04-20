using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Features.Trace.Infrastructure;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Trace;

public static class TraceServiceCollectionExtensions
{
    public static IServiceCollection AddTrace(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<IQueryHandler<TraceQuery, CodeNodeResult[]>, TraceQueryHandler>();
        return services;
    }
}
