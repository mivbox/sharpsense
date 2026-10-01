namespace SharpSense.Cli.Ui.Api;

public sealed record MergeWorkspacesRequest(string Name, Guid[] WorkspaceIds);
