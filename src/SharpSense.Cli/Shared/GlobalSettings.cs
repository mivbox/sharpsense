using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Shared;

internal abstract class GlobalSettings : CliSettings
{
    [CommandOption("-w|--workspace <name-or-id>")]
    [Description("Select a registered workspace by name or ID.")]
    public string? Workspace
    {
        get; init;
    }

    [CommandOption("--workspace-root|--repo-root <path>")]
    [Description("Base directory for workspace setup, UI browsing or MCP discovery; does not select a CLI workspace.")]
    public string? WorkspaceRoot
    {
        get; init;
    }

    public override ValidationResult Validate() =>
        Workspace is not null && string.IsNullOrWhiteSpace(Workspace)
        ? ValidationResult.Error("--workspace must contain a workspace name or ID.")
        : ValidationResult.Success();
}
