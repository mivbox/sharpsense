using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Context360.Abstractions;

namespace SharpSense.Application.Context360;

public static class Context360ServiceCollectionExtensions
{
    public static IServiceCollection AddContext360(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IContextService, ContextService>();
        return services;
    }
}
