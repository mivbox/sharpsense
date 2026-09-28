using SharpSense.Application.Indexing;

namespace SharpSense.Application.Shared.Options;

public sealed class WorkspaceExecutionOptions
{
    public string RepositoryRoot
    {
        get; set;
    } = string.Empty;

    public string? WorkspaceId
    {
        get; set;
    }

    public IReadOnlyList<WorkspaceSource> WorkspaceSources
    {
        get; set;
    } = [];

    public bool SkipEmbeddings
    {
        get; set;
    }

    public bool DisableEmbeddingCache
    {
        get; set;
    }
}
