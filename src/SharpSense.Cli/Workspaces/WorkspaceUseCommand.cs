using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceUseCommand : WorkspaceCatalogCommand<WorkspaceUseCommand.Settings>
{
    public sealed class Settings : CliSettings
    {
        [CommandArgument(0, "<name-or-id>")]
        [Description("Workspace to use by default from any directory.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--json")]
        [Description("Write the selected workspace as JSON.")]
        public bool Json { get; init; }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .Use(settings.Name);
        WorkspaceOutput.Write(context, selection, settings.Json, "Selected default");

        return Task.FromResult(0);
    }
}
