using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceAddCommand : WorkspaceCatalogCommand<WorkspaceMutationSettings>
{
    protected override Task<int> Execute(
        CommandContext context,
        WorkspaceMutationSettings settings,
        IHost host,
        CancellationToken ct)
    {
        var sourceBase = CommandPathResolver.ResolveWorkspaceRoot(settings.WorkspaceRoot);
        var selection = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .AddSources(settings.Name, settings.GetSources(sourceBase));
        WorkspaceOutput.Write(context, selection, settings.Json, "Updated");

        return Task.FromResult(0);
    }
}
