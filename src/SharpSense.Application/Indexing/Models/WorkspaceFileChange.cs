using JetBrains.Annotations;

namespace SharpSense.Application.Indexing.Models;

[PublicAPI]
public sealed record WorkspaceFileChange(
    WorkspaceFileChangeAction ActionType,
    string? OldPath = null,
    string? NewPath = null)
{
    public IEnumerable<string> GetAffectedPaths()
    {
        if (!string.IsNullOrWhiteSpace(OldPath))
        {
            yield return OldPath;
        }

        if (string.IsNullOrWhiteSpace(NewPath))
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(OldPath) ||
            !string.Equals(OldPath, NewPath, GetPathComparison()))
        {
            yield return NewPath;
        }
    }

    public string? GetCurrentPath()
    {
        return ActionType switch
        {
            WorkspaceFileChangeAction.Deleted => null,
            _ => !string.IsNullOrWhiteSpace(NewPath) ? NewPath : OldPath
        };
    }

    private static StringComparison GetPathComparison()
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
