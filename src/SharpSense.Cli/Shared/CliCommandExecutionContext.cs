using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace SharpSense.Cli.Shared;

internal sealed record CliCommandExecutionContext(
    IAnsiConsole? Console,
    Action<IServiceCollection>? ConfigureServices,
    bool EnableFileLogging);
