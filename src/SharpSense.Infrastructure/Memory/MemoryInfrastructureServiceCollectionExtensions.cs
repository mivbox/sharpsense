using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Memory.Abstractions;

namespace SharpSense.Infrastructure.Memory;

public static class MemoryInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddMemoryInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<MemoryStore>();
        services.TryAddScoped<IMemoryReader>(serviceProvider => serviceProvider.GetRequiredService<MemoryStore>());
        services.TryAddScoped<IMemoryWriter>(serviceProvider => serviceProvider.GetRequiredService<MemoryStore>());

        return services;
    }
}
