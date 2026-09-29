using SharpSense.Application.Indexing;

namespace SharpSense.Infrastructure.Storage;

/// <summary>Manages named workspace definitions and exclusive indexing leases in the SharpSense home directory.</summary>
public interface IWorkspaceCatalog
{
    Guid? GetDefaultWorkspaceId();
    WorkspaceSelection Use(string nameOrId);
    IDisposable AcquireIndexLease(WorkspaceSelection selection);
    IReadOnlyList<WorkspaceSelection> List();

    /// <summary>Resolves an explicit selector or the saved CLI default. Never infers a workspace from a directory.</summary>
    WorkspaceSelection Resolve(string? nameOrId);

    /// <summary>Finds one workspace containing the directory. Never reads the saved CLI default.</summary>
    WorkspaceSelection ResolveFromDirectory(string workingDirectory);

    WorkspaceSelection ResolveById(Guid id);
    WorkspaceSelection Create(string name, string workspaceRoot, IEnumerable<WorkspaceSource> sources);
    WorkspaceSelection AddSources(string nameOrId, IEnumerable<WorkspaceSource> sources);
    WorkspaceSelection Rename(string nameOrId, string name);
    WorkspaceSelection Update(string nameOrId, string name, IEnumerable<WorkspaceSource> sources);
    WorkspaceSourceRemoval RemoveSources(string nameOrId, IEnumerable<WorkspaceSource> sources);
    WorkspaceSelection Merge(string name, IEnumerable<string> workspaceNames);
}
