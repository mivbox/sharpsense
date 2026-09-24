using Spectre.Console.Cli;

namespace SharpSense.Cli.Shared;

/// <summary>Spectre branch defaults do not expose WithData; retain the same per-app context for those commands.</summary>
internal sealed class CliExecutionContextInterceptor(CliCommandExecutionContext executionContext) : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (settings is GlobalSettings global)
        {
            global.ExecutionContext = executionContext;
        }
    }

    public void InterceptResult(CommandContext context, CommandSettings settings, ref int result) { }
}
