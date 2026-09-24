using Spectre.Console.Cli;

namespace SharpSense.Cli.Shared;

internal static class CommandOutput
{
    public static CliCommandExecutionContext? GetExecutionContext(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Data as CliCommandExecutionContext;
    }

    public static void Write(CommandContext context, string output)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(output);

        var console = GetExecutionContext(context)?.Console;
        if (console is not null)
        {
            console.Profile.Out.Writer.Write(output);
            console.Profile.Out.Writer.Flush();
            return;
        }

        Spectre.Console.AnsiConsole.Console.Profile.Out.Writer.Write(output);
        Spectre.Console.AnsiConsole.Console.Profile.Out.Writer.Flush();
    }

    public static void WriteError(CommandContext context, string message)
    {
        var console = GetExecutionContext(context)?.Console;
        if (console is not null)
        {
            console.Profile.Out.Writer.WriteLine($"Error: {message}");
            console.Profile.Out.Writer.Flush();
            return;
        }

        Console.Error.WriteLine($"Error: {message}");
    }
}
