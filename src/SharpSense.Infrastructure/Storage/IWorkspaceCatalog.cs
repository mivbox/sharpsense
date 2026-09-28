using SharpSense.Application.Indexing;

namespace SharpSense.Infrastructure.Storage;

/// <summary>Manages named workspace definitions and exclusive indexing leases in the SharpSense home directory.</summary>
public interface IWorkspaceCatalog
{
    string HomeDirectory
    {
        get;
    }
    Guid? GetDefaultWorkspaceId();
    WorkspaceSelection Use(string nameOrId);
    IDisposable AcquireIndexLease(WorkspaceSelection selection);
    IReadOnlyList<WorkspaceSelection> List();
    WorkspaceSelection Resolve(string? nameOrId, string workingDirectory);
    WorkspaceSelection ResolveById(Guid id);
    WorkspaceSelection Create(string name, string repositoryRoot, IEnumerable<WorkspaceSource> sources);
    WorkspaceSelection AddSources(string nameOrId, IEnumerable<WorkspaceSource> sources);
    WorkspaceSelection Rename(string nameOrId, string name);
    WorkspaceSelection Update(string nameOrId, string name, IEnumerable<WorkspaceSource> sources);
    WorkspaceSourceRemoval RemoveSources(string nameOrId, IEnumerable<WorkspaceSource> sources);
    WorkspaceSelection Merge(string name, IEnumerable<string> workspaceNames);
}
