using Microsoft.CodeAnalysis;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed record MsBuildDiagnosticReport(
    IReadOnlyList<MsBuildMessage> Warnings,
    IReadOnlyList<MsBuildMessage> Errors)
{
    public bool IsConfirmedWarning(WorkspaceDiagnostic diagnostic)
        => diagnostic.Kind == WorkspaceDiagnosticKind.Failure &&
           Warnings.Any(warning => warning.Matches(diagnostic)) &&
           !Errors.Any(error => error.Matches(diagnostic));
}
