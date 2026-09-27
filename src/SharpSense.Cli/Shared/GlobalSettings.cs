using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Shared;

public abstract class GlobalSettings : CliSettings
{
    [CommandOption("-w|--workspace <name-or-id>")]
    [Description("Select a registered workspace by name or ID, overriding the saved default.")]
    public string? Workspace { get; init; }

    [CommandOption("--repo-root <path>")]
    [Description("Use this directory for workspace lookup when no default or --workspace is set.")]
    public string? RepositoryRoot { get; init; }

    public override ValidationResult Validate() =>
        Workspace is not null && string.IsNullOrWhiteSpace(Workspace)
            ? ValidationResult.Error("--workspace must contain a workspace name or ID; omit it to use the default.")
            : ValidationResult.Success();
}
