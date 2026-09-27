using Microsoft.Extensions.DependencyInjection;
using SharpSense.Cli.Shared;

namespace SharpSense.Cli.Ui.Api;

internal sealed record WorkspaceUiOptions(string? InitialWorkspace, string WorkingDirectory, Uri Address);

internal static class WorkspaceUiServices
{
    public static IServiceCollection AddWorkspaceUiServices(this IServiceCollection services, WorkspaceUiOptions options)
    {
        services.AddSingleton(options);

        return services.AddWorkspaceExecutionServices();
    }
}
