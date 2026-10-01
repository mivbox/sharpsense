using Microsoft.Extensions.DependencyInjection;
using SharpSense.Cli.Shared;

namespace SharpSense.Cli.Ui.Api;

internal static class WorkspaceUiServiceCollectionExtensions
{
    public static IServiceCollection AddWorkspaceUi(
        this IServiceCollection services,
        WorkspaceUiOptions options)
    {
        services.AddSingleton(options);

        return services.AddWorkspaceExecution();
    }
}
