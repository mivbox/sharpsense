using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceMergeCommand : WorkspaceCatalogCommand<WorkspaceMergeCommand.Settings>
{
    public sealed class Settings : CliSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = string.Empty;

        [CommandArgument(1, "<workspaces>")]
        public string[] Workspaces { get; init; } = [];

        [CommandOption("--json")]
        [Description("Write machine-readable JSON without interactive prompts.")]
        public bool Json { get; init; }

        public override ValidationResult Validate() => Workspaces.Length < 2
            ? ValidationResult.Error("Select at least two existing workspaces to merge.")
            : ValidationResult.Success();
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .Merge(settings.Name, settings.Workspaces);
        WorkspaceOutput.Write(context, selection, settings.Json, "Created");

        return Task.FromResult(0);
    }
}
