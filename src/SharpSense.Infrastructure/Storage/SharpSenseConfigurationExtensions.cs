using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing;

namespace SharpSense.Infrastructure.Storage;

public static class SharpSenseConfigurationExtensions
{
    /// <summary>
    /// Binds a configuration snapshot for the selected workspace. Changes are picked up by the next command or host.
    /// No project-local configuration files are read or watched.
    /// </summary>
    public static IServiceCollection AddSharpSenseConfiguration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<SharpSenseConfig>()
            .Configure<WorkspaceSelection>((options, selection) =>
            {
                options.IncludePaths = selection.Definition.Sources
                    .Where(static source => source.Kind == WorkspaceSourceKind.Markdown)
                    .Select(static source => source.Path)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            });

        return services;
    }

    public static IServiceCollection AddSharpSenseConfiguration(
        this IServiceCollection services,
        WorkspaceSelection selection) => services.AddRepositoryWorkspace(selection).AddSharpSenseConfiguration();
}
