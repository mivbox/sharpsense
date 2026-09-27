using Spectre.Console.Cli;
using System.ComponentModel;

namespace SharpSense.Cli.Shared;

internal abstract class CliSettings : CommandSettings
{
    [CommandOption("-v|--verbose")]
    [Description("Include diagnostic details in the command log.")]
    public bool IsVerbose
    {
        get; init;
    }
}
