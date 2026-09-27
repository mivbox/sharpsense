using FluentResults;
using System.Text;

namespace SharpSense.Infrastructure.CommandExecution;

internal static class CommandInvocationParser
{
    public static Result<ParsedCommand> Parse(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command))
        {
            return Result.Fail<ParsedCommand>("Command must not be empty.");
        }

        var current = new StringBuilder();
        var tokens = new List<string>();
        char? quote = null;
        var tokenStarted = false;

        for (var index = 0; index < command.Length; index++)
        {
            var currentCharacter = command[index];
            if (quote is not null)
            {
                if (quote == '"' &&
                    currentCharacter == '\\' &&
                    index + 1 < command.Length &&
                    command[index + 1] is '"' or '\\')
                {
                    current.Append(command[index + 1]);
                    index++;
                    tokenStarted = true;
                    continue;
                }

                if (currentCharacter == quote)
                {
                    quote = null;
                    continue;
                }

                current.Append(currentCharacter);
                tokenStarted = true;
                continue;
            }

            if (currentCharacter is '"' or '\'')
            {
                quote = currentCharacter;
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(currentCharacter))
            {
                FlushToken();
                continue;
            }

            current.Append(currentCharacter);
            tokenStarted = true;
        }

        if (quote is not null)
        {
            return Result.Fail<ParsedCommand>("Command contains an unmatched quote.");
        }

        FlushToken();
        return tokens.Count == 0
            ? Result.Fail<ParsedCommand>("Command must not be empty.")
            : Result.Ok(new ParsedCommand(tokens[0], [.. tokens.Skip(1)]));

        void FlushToken()
        {
            if (!tokenStarted)
            {
                return;
            }

            tokens.Add(current.ToString());
            current.Clear();
            tokenStarted = false;
        }
    }
}

internal sealed record ParsedCommand(
    string Executable,
    string[] Arguments);
