namespace SharpSense.Cli.Ui.Api;

public sealed record WorkspaceSummary(
    Guid Id,
    string Name,
    string RepositoryRoot,
    IReadOnlyList<WorkspaceSourceOverview> Sources);
