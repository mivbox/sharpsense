using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Context360.Abstractions;

namespace SharpSense.Infrastructure.Context360;

public static class Context360InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddContext360Infrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IContextRepository, ContextRepository>();

        return services;
    }
}
