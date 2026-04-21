using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Features.Trace.Infrastructure;

namespace SharpSense.Infrastructure.Trace;

public static class TraceInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddTraceInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ITraceNavigator, TraceNavigator>();
        return services;
    }
}
