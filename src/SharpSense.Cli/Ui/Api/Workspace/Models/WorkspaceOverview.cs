namespace SharpSense.Cli.Ui.Api;

public sealed record WorkspaceOverview(
    string Name,
    string RepositoryRoot,
    bool Indexed,
    int ProjectCount,
    int DocumentCount,
    int NodeCount,
    int EdgeCount,
    int MemoryCount,
    int DirectoryCount,
    Guid? WorkspaceId = null,
    IReadOnlyList<WorkspaceSourceOverview>? Sources = null);
