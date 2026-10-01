using Microsoft.CodeAnalysis;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed record MsBuildMessage(string? ProjectFile, string? Code, string? Message)
{
    public bool Matches(WorkspaceDiagnostic diagnostic)
    {
        // Match an observed typed event's complete message and owning project. Do not
        // infer severity from English words, diagnostic codes, or an empty error list.
        if (string.IsNullOrWhiteSpace(ProjectFile) || string.IsNullOrWhiteSpace(Message))
        {
            return false;
        }

        var pathComparison = FileSystemPaths.Comparison;

        return diagnostic.Message.Contains(
            $"'{ProjectFile}'",
            pathComparison) &&
            diagnostic.Message.EndsWith(
                $": {Message}",
                StringComparison.Ordinal);
    }

    public string Format(string severity) => $"MSBuild {severity} {Code} in '{ProjectFile}': {Message}";
}
