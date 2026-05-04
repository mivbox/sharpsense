using Microsoft.CodeAnalysis.Text;
using SharpSense.Application.Refactoring.Models;

namespace SharpSense.Infrastructure.Refactoring;

internal static class RefactorTextSpanResolver
{
    public static RefactorTextSpanResolution Resolve(
        SourceText sourceText,
        NodeRefactorTarget target)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(target);

        if (target.StartLine <= 0)
        {
            return Failure($"Persisted start line '{target.StartLine}' must be greater than zero.");
        }

        if (target.EndLine < target.StartLine)
        {
            return Failure($"Persisted line span '{target.StartLine}-{target.EndLine}' is invalid.");
        }

        var lines = sourceText.Lines;
        if (target.StartLine > lines.Count || target.EndLine > lines.Count)
        {
            return Failure(
                $"Persisted line span '{target.StartLine}-{target.EndLine}' falls outside '{target.RelativeFilePath}', which has {lines.Count} line(s).");
        }

        var startLineIndex = target.StartLine - 1;
        var endLineIndex = target.EndLine - 1;
        var startPosition = lines[startLineIndex].Start;
        var endPosition = lines[endLineIndex].End;

        return new RefactorTextSpanResolution(
            true,
            TextSpan.FromBounds(startPosition, endPosition),
            string.Empty);
    }

    private static RefactorTextSpanResolution Failure(string errorMessage)
        => new(
            false,
            default,
            errorMessage);
}

internal readonly record struct RefactorTextSpanResolution(
    bool Success,
    TextSpan TextSpan,
    string ErrorMessage);
