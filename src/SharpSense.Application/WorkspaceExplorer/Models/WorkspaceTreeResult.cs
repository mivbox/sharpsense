namespace SharpSense.Application.WorkspaceExplorer.Models;

public sealed record WorkspaceTreeResult(
    string ParentPath,
    WorkspaceTreeNode[] Nodes,
    int? ParentDirectoryId = null);
