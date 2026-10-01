using SharpSense.Application.Indexing;

namespace SharpSense.Cli.Ui.Api;

public sealed record CreateWorkspaceRequest(string Name, string RepositoryRoot, WorkspaceSource[] Sources);
