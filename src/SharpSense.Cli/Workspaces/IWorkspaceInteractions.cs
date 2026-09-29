using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Workspaces;

/// <summary>Terminal choices only; indexing and web transports never depend on prompts.</summary>
internal interface IWorkspaceInteractions
{
    bool IsInteractive
    {
        get;
    }
    Task<WorkspaceSelection?> SelectWorkspace(IReadOnlyList<WorkspaceSelection> choices, CancellationToken ct);
    Task<string> ReadName(string? current, CancellationToken ct);
    Task<string> ReadWorkspaceRoot(string current, CancellationToken ct);
    Task<IReadOnlyList<WorkspaceSource>> SelectSources(string root, CancellationToken ct);
    Task<bool> Confirm(string message, CancellationToken ct);
    void ShowConfiguration(string name, string root, IReadOnlyList<WorkspaceSource> sources);
}
