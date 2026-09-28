namespace SharpSense.Infrastructure.Storage;

/// <summary>Pins workspace services to one immutable selection for a request or analysis session.</summary>
public interface IWorkspaceScope
{
    WorkspaceSelection Selection
    {
        get;
    }
    bool SkipEmbeddings
    {
        get;
    }
    bool DisableEmbeddingCache
    {
        get;
    }
    void Bind(WorkspaceSelection selection, bool skipEmbeddings = false, bool disableEmbeddingCache = false);
}
