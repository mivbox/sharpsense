using SharpSense.Application.Indexing;

namespace SharpSense.Application.Shared.Options;

public sealed class SharpSenseCliOptions
{
    public string RepositoryRoot { get; set; } = string.Empty;

    public string? TargetPath { get; set; }

    public string? WorkspaceId { get; set; }

    public IReadOnlyList<WorkspaceSource> WorkspaceSources { get; set; } = [];

    public bool Watch { get; set; }

    public bool SkipEmbeddings { get; set; }

    public bool DisableEmbeddingCache { get; set; }
}
