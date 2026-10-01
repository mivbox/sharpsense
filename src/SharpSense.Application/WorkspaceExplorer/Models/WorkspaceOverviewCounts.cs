namespace SharpSense.Application.WorkspaceExplorer.Models;

public sealed record WorkspaceOverviewCounts(
    int Projects,
    int Documents,
    int Nodes,
    int Edges,
    int Memories,
    int Directories);
