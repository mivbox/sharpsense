namespace SharpSense.Cli.Ui.Api;

public sealed record WorkspaceDiscoveryResponse(string RepositoryRoot, IReadOnlyList<WorkspaceSourceOverview> Sources);
