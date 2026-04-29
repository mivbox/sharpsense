namespace SharpSense.Application.WorkspaceExplorer.Models;

public sealed record WorkspaceTreeNode(
    string Id,
    string? ParentId,
    string Path,
    string Label,
    string Kind,
    bool HasChildren,
    int? ChildCount,
    bool IsSelectable);
