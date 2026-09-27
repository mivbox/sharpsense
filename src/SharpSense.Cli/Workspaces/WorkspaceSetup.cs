using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceSetup(IWorkspaceCatalog catalog, IWorkspaceInteractions interactions)
{
    public async Task<WorkspaceSelection?> SelectForAnalysis(string? selector, string root, CancellationToken ct)
    {
        if (selector is not null || !interactions.IsInteractive || catalog.GetDefaultWorkspaceId() is not null)
        {
            return catalog.Resolve(selector, root);
        }

        try
        {
            return catalog.Resolve(null, root);
        }
        catch (InvalidOperationException)
        {
            return await interactions.SelectWorkspace(catalog.List(), ct) ?? await Create(null, root, [], ct);
        }
    }

    public async Task<WorkspaceSelection?> Create(string? name, string root, IReadOnlyList<WorkspaceSource> sources, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(name) && sources.Count > 0)
        {
            return catalog.Create(name, root, sources);
        }

        if (!interactions.IsInteractive)
        {
            throw new InvalidOperationException(
                "Provide a workspace name and --csharp, --typescript, or --markdown sources, or run 'workspace create' in an interactive terminal.");
        }

        name = string.IsNullOrWhiteSpace(name) ? await interactions.ReadName(null, ct) : name;
        if (sources.Count == 0)
        {
            root = Path.GetFullPath(await interactions.ReadRepositoryRoot(root, ct), root);
            sources = await interactions.SelectSources(root, ct);
        }

        interactions.ShowConfiguration(name, root, sources);

        return await interactions.Confirm("Save this workspace?", ct) ? catalog.Create(name, root, sources) : null;
    }
}
