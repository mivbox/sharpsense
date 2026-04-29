namespace SharpSense.Application.WorkspaceExplorer.Models;

public sealed record WorkspaceTreeNode(
    int Id,
    int? ParentId,
    string Path,
    string Label,
    string Kind,
    bool HasChildren,
    int? ChildCount,
    bool IsSelectable);
