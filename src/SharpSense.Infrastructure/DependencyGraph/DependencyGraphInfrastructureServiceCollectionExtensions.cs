using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Features.DependencyGraph.Infrastructure;

namespace SharpSense.Infrastructure.DependencyGraph;

public static class DependencyGraphInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDependencyGraphInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IDependencyGraphRepository, DependencyGraphRepository>();

        return services;
    }
}
