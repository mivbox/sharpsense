using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Refactoring;

public static class RefactoringInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddRefactoringInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddFileSystem();
        services.TryAddSingleton<WorkspaceTargetResolver>();
        services.TryAddScoped<IRefactorTargetLookup, RefactorTargetLookup>();
        services.TryAddScoped<IWorkspaceRefactorer, RoslynWorkspaceRefactorer>();
        return services;
    }
}
