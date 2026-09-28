using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Inheritors.Abstractions;

namespace SharpSense.Infrastructure.Inheritors;

public static class InheritorsInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInheritorsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IInheritorFinder, InheritorFinder>();

        return services;
    }
}
