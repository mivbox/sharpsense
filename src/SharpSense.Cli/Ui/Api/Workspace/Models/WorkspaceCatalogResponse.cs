namespace SharpSense.Cli.Ui.Api;

public sealed record WorkspaceCatalogResponse(IReadOnlyList<WorkspaceSummary> Workspaces, Guid? InitialWorkspaceId);
