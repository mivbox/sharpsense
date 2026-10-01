using Microsoft.CodeAnalysis;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed record WorkspaceLoadResult(
    Solution Solution,
    IReadOnlyList<string> Diagnostics);
