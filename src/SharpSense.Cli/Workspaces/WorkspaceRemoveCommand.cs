using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceRemoveCommand : WorkspaceCatalogCommand<WorkspaceMutationSettings>
{
    protected override Task<int> Execute(
        CommandContext context,
        WorkspaceMutationSettings settings,
        IHost host,
        CancellationToken ct)
    {
        var sourceBase = CommandPathResolver.ResolveWorkspaceRoot(settings.WorkspaceRoot);
        var removal = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .RemoveSources(settings.Name, settings.GetSources(sourceBase));
        WorkspaceOutput.WriteRemoval(context, removal, settings.Json);

        return Task.FromResult(0);
    }
}
