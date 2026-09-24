using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharpSense.Application.Indexing;
using SharpSense.Application.Shared.Options;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Shared;

internal static class WorkspaceExecutionServices
{
    public static IServiceCollection AddWorkspaceExecutionServices(this IServiceCollection services)
    {
        services.AddFileSystem();
        services.TryAddSingleton(provider => new WorkspaceCatalog(provider.GetRequiredService<IFileSystem>()));
        services.AddScoped<WorkspaceScope>();
        services.AddScoped(provider => provider.GetRequiredService<WorkspaceScope>().Selection);
        services.AddScoped(provider => provider.GetRequiredService<WorkspaceSelection>().Workspace);
        services.AddScoped<IOptions<SharpSenseCliOptions>>(provider =>
        {
            var scope = provider.GetRequiredService<WorkspaceScope>();
            var selection = scope.Selection;
            return Options.Create(new SharpSenseCliOptions
            {
                WorkspaceId = selection.Definition.Id.ToString(),
                RepositoryRoot = selection.Workspace.RootPath,
                TargetPath = selection.Workspace.RootPath,
                WorkspaceSources = selection.Definition.Sources,
                Watch = scope.Watch,
                SkipEmbeddings = scope.SkipEmbeddings,
                DisableEmbeddingCache = scope.DisableEmbeddingCache
            });
        });
        services.AddScoped<IOptionsMonitor<SharpSenseConfig>>(provider =>
            new FixedOptionsMonitor<SharpSenseConfig>(new SharpSenseConfig
            {
                IncludePaths = provider.GetRequiredService<WorkspaceSelection>().Definition.Sources
                    .Where(static source => source.Kind == WorkspaceSourceKind.Markdown)
                    .Select(static source => source.Path)
                    .ToArray()
            }));
        services.AddPersistence(initializeOnStartup: false, factoryLifetime: ServiceLifetime.Scoped);

        return services;
    }

    private sealed class FixedOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
