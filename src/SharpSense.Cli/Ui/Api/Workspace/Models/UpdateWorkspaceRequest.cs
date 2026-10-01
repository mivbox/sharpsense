using SharpSense.Application.Indexing;

namespace SharpSense.Cli.Ui.Api;

public sealed record UpdateWorkspaceRequest(string Name, WorkspaceSource[] Sources);
