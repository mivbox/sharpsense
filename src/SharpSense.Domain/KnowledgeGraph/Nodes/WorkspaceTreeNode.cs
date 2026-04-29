using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Domain.KnowledgeGraph.Nodes;

public sealed class WorkspaceTreeNode
{
    public string Id { get; set; } = string.Empty;

    public string? ParentId { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public WorkspaceTreeNodeKind Kind { get; set; } = WorkspaceTreeNodeKind.File;

    public string? ProjectId { get; set; }

    public bool HasChildren { get; set; }

    public int? ChildCount { get; set; }

    public bool IsSelectable { get; set; }
}
