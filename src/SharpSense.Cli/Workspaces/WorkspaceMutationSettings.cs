using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceMutationSettings : WorkspaceSourceSettings
{
    [CommandArgument(0, "<name-or-id>")]
    public string Name { get; init; } = string.Empty;

    public override ValidationResult Validate() => !HasSources
        ? ValidationResult.Error("Specify at least one --csharp, --typescript, or --markdown source.")
        : ValidationResult.Success();
}
