using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.DependencyGraph.Abstractions;

namespace SharpSense.Infrastructure.DependencyGraph;

public static class DependencyGraphInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDependencyGraphInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IGraphPageRepository, GraphPageRepository>();

        return services;
    }
}
