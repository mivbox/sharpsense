using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Context360.GetNodeContext;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Context360;

public static class Context360ServiceCollectionExtensions
{
    public static IServiceCollection AddContext360(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<IQueryHandler<GetNodeContextQuery, Context360Result>, GetNodeContextQueryHandler>();
        return services;
    }
}
