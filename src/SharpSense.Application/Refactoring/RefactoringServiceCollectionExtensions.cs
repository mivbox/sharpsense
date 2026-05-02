using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Refactoring.Abstractions;

namespace SharpSense.Application.Refactoring;

public static class RefactoringServiceCollectionExtensions
{
    public static IServiceCollection AddRefactoring(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<INodeRefactorer, NodeRefactorer>();
        return services;
    }
}
