using Spectre.Console.Cli;

namespace SharpSense.Cli.Shared;

public abstract class GlobalSettings : CommandSettings
{
    [CommandOption("-v|--verbose")]
    public bool IsVerbose { get; init; }
}
