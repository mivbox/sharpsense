using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpSense.Application.Shared.Options;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Shared;

internal static class WorkspaceExecutionServices
{
    public static IServiceCollection AddWorkspaceExecutionServices(this IServiceCollection services)
    {
        services.AddFileSystem();
        services.AddWorkspaceCatalog();
        services.AddScoped(provider => provider.GetRequiredService<IWorkspaceScope>().Selection);
        services.AddScoped(provider => provider.GetRequiredService<WorkspaceSelection>().Workspace);
        services.AddScoped<IOptions<WorkspaceExecutionOptions>>(provider =>
        {
            var scope = provider.GetRequiredService<IWorkspaceScope>();
            var selection = scope.Selection;

            return Options.Create(new WorkspaceExecutionOptions
            {
                WorkspaceId = selection.Definition.Id.ToString(),
                RepositoryRoot = selection.Workspace.RootPath,
                WorkspaceSources = selection.Definition.Sources,
                SkipEmbeddings = scope.SkipEmbeddings,
                DisableEmbeddingCache = scope.DisableEmbeddingCache
            });
        });
        services.AddPersistence(initializeOnStartup: false, factoryLifetime: ServiceLifetime.Scoped);

        return services;
    }
}
