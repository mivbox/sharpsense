using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Inheritors.GetInheritors;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Inheritors;

public static class InheritorsServiceCollectionExtensions
{
    public static IServiceCollection AddInheritors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<IQueryHandler<GetInheritorsQuery, CodeNodeResult[]>, GetInheritorsQueryHandler>();
        return services;
    }
}
