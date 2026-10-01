using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceShowCommand : WorkspaceCatalogCommand<WorkspaceShowCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[name-or-id]")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        [Description("Write machine-readable JSON without interactive prompts.")]
        public bool Json { get; init; }

        public override ValidationResult Validate()
        {
            if (Name is not null && Workspace is not null)
            {
                return ValidationResult.Error("Specify either a workspace argument or --workspace, not both.");
            }

            return Name is not null && string.IsNullOrWhiteSpace(Name)
                ? ValidationResult.Error("Workspace name or ID must not be empty or whitespace.")
                : base.Validate();
        }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .Resolve(settings.Name ?? settings.Workspace);
        WorkspaceOutput.Write(context, selection, settings.Json, "Selected");

        return Task.FromResult(0);
    }
}
