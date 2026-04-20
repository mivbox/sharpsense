using SharpSense.Application.Shared.Models;

namespace SharpSense.Cli.Shared;

internal interface IOutputFormatter
{
    string Format(IEnumerable<CodeNodeResult> nodes);
}
