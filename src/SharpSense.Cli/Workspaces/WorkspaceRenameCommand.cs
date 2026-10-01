using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceRenameCommand : WorkspaceCatalogCommand<WorkspaceRenameCommand.Settings>
{
    public sealed class Settings : CliSettings
    {
        [CommandArgument(0, "<name-or-id>")]
        public string Name { get; init; } = string.Empty;

        [CommandArgument(1, "<new-name>")]
        public string NewName { get; init; } = string.Empty;

        [CommandOption("--json")]
        [Description("Write machine-readable JSON without interactive prompts.")]
        public bool Json { get; init; }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var catalog = host.Services.GetRequiredService<IWorkspaceCatalog>();
        var updated = catalog.Rename(settings.Name, settings.NewName);
        WorkspaceOutput.Write(context, updated, settings.Json, "Renamed");

        return Task.FromResult(0);
    }
}
