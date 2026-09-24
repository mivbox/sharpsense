using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Analyze;

internal enum WorkspaceAction
{
    Inspect,
    Select,
    Create,
    Rename,
    AddSources,
    RemoveSources,
    Merge,
    Analyze,
    Watch,
    Exit
}

/// <summary>Terminal choices only; indexing and web transports never depend on prompts.</summary>
internal interface IAnalyzeInteractions
{
    bool IsInteractive { get; }
    Task<WorkspaceSelection?> SelectWorkspace(IReadOnlyList<WorkspaceSelection> choices, CancellationToken ct);
    Task<WorkspaceAction> SelectAction(WorkspaceSelection? selection, CancellationToken ct);
    Task<string> ReadName(string? current, CancellationToken ct);
    Task<string> ReadRepositoryRoot(string current, CancellationToken ct);
    Task<IReadOnlyList<WorkspaceSource>> SelectSources(string root, CancellationToken ct);
    Task<IReadOnlyList<WorkspaceSource>> SelectSourcesToRemove(IReadOnlyList<WorkspaceSource> sources, CancellationToken ct);
    Task<IReadOnlyList<WorkspaceSelection>> SelectWorkspacesToMerge(IReadOnlyList<WorkspaceSelection> choices, CancellationToken ct);
    Task<bool> Confirm(string message, CancellationToken ct);
    void ShowConfiguration(string name, string root, IReadOnlyList<WorkspaceSource> sources);
    void ShowWorkspace(WorkspaceSelection selection);
    void ShowError(string message);
}
