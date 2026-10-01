using SharpSense.Application.Indexing;

namespace SharpSense.Cli.Workspaces;

internal sealed record DiscoveredWorkspaceSources(string WorkspaceRoot, IReadOnlyList<WorkspaceSource> Sources);
