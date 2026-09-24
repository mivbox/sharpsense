using Spectre.Console.Cli;

namespace SharpSense.Cli.Shared;

public abstract class GlobalSettings : CommandSettings
{
    [CommandOption("-v|--verbose")]
    public bool IsVerbose { get; init; }

    [CommandOption("-w|--workspace <name-or-id>")]
    public string? Workspace { get; init; }

    [CommandOption("--repo-root <path>")]
    public string? RepositoryRoot { get; init; }
}
