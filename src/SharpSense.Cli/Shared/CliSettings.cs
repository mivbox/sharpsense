using System.ComponentModel;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Shared;

public abstract class CliSettings : CommandSettings
{
    [CommandOption("-v|--verbose")]
    [Description("Include diagnostic details in the command log.")]
    public bool IsVerbose { get; init; }
}
