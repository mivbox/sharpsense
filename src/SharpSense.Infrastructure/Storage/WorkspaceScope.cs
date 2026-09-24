namespace SharpSense.Infrastructure.Storage;

/// <summary>
/// Pins a request or background indexing job to one workspace for its entire service scope.
/// </summary>
public sealed class WorkspaceScope
{
    private WorkspaceSelection? _selection;

    public WorkspaceSelection Selection => _selection
        ?? throw new InvalidOperationException("A workspace must be selected before resolving workspace services.");

    public bool Watch { get; private set; }

    public bool SkipEmbeddings { get; private set; }

    public bool DisableEmbeddingCache { get; private set; }

    public void Bind(WorkspaceSelection selection, bool watch = false, bool skipEmbeddings = false, bool disableEmbeddingCache = false)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (_selection is not null)
        {
            throw new InvalidOperationException("A workspace scope cannot change its selection after binding.");
        }

        _selection = selection;
        Watch = watch;
        SkipEmbeddings = skipEmbeddings;
        DisableEmbeddingCache = disableEmbeddingCache;
    }
}
