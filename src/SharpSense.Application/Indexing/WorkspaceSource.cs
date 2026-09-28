namespace SharpSense.Application.Indexing;

/// <summary>
/// Selects repository-relative project, TypeScript configuration/directory, or Markdown glob input.
/// </summary>
public sealed record WorkspaceSource(WorkspaceSourceKind Kind, string Path)
{
    public WorkspaceSource() : this(default, string.Empty)
    {
    }
}

public enum WorkspaceSourceKind
{
    CSharp,
    TypeScript,
    Markdown
}
