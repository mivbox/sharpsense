using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceCreateCommand : WorkspaceCatalogCommand<WorkspaceCreateCommand.Settings>
{
    public sealed class Settings : WorkspaceSourceSettings
    {
        [CommandArgument(0, "[name]")]
        [Description("Name for the new workspace. Prompts when omitted in an interactive terminal.")]
        public string? Name { get; init; }
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        var root = CommandPathResolver.ResolveWorkspaceRoot(settings.WorkspaceRoot);
        var sources = settings.GetSources(root);
        if (settings.Json && (string.IsNullOrWhiteSpace(settings.Name) || sources.Length == 0))
        {
            throw new InvalidOperationException("JSON mode requires a workspace name and explicit sources.");
        }

        var selection = await host.Services.GetRequiredService<WorkspaceSetup>()
            .Create(settings.Name, root, sources, ct);
        if (selection is not null)
        {
            WorkspaceOutput.Write(context, selection, settings.Json, "Created");
        }

        return 0;
    }
}
