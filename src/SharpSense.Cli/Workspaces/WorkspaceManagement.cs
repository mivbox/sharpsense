using SharpSense.Application.Indexing;
using SharpSense.Cli.Analyze;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Workspaces;

internal sealed class WorkspaceManagement(WorkspaceCatalog catalog, IAnalyzeInteractions interactions)
{
    public async Task<WorkspaceSelection?> SelectForAnalysis(string? selector, string root, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(selector) || !interactions.IsInteractive)
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
        if (!interactions.IsInteractive)
        {
            if (string.IsNullOrWhiteSpace(name) || sources.Count == 0)
            {
                throw new InvalidOperationException("Provide a workspace name and --csharp, --typescript, or --markdown sources, or run configure in an interactive terminal.");
            }
            return catalog.Create(name, root, sources);
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

    public async Task Manage(string? selector, string root, Func<WorkspaceSelection, bool, CancellationToken, Task<int>> analyze, CancellationToken ct)
    {
        if (!interactions.IsInteractive)
        {
            throw new InvalidOperationException("Workspace manager requires an interactive terminal. Use 'workspace list', 'create', 'show', 'rename', 'add', 'remove', or 'merge' for automation.");
        }

        WorkspaceSelection? selected = string.IsNullOrWhiteSpace(selector) ? null : catalog.Resolve(selector, root);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var action = await interactions.SelectAction(selected, ct);
                switch (action)
                {
                    case WorkspaceAction.Exit:
                        return;
                    case WorkspaceAction.Create:
                        selected = await Create(null, root, [], ct) ?? selected;
                        break;
                    case WorkspaceAction.Select:
                        selected = await interactions.SelectWorkspace(catalog.List(), ct) ?? await Create(null, root, [], ct) ?? selected;
                        break;
                    case WorkspaceAction.Inspect:
                        interactions.ShowWorkspace(selected!);
                        break;
                    case WorkspaceAction.Rename:
                        var name = await interactions.ReadName(selected!.Definition.Name, ct);
                        if (await interactions.Confirm($"Rename '{selected.Definition.Name}' to '{name}'?", ct))
                        {
                            selected = catalog.Rename(selected.Definition.Id.ToString(), name);
                        }
                        break;
                    case WorkspaceAction.AddSources:
                        var sources = await interactions.SelectSources(selected!.Definition.RepositoryRoot, ct);
                        interactions.ShowConfiguration(selected.Definition.Name, selected.Definition.RepositoryRoot, selected.Definition.Sources.Concat(sources).ToArray());
                        if (await interactions.Confirm("Save these workspace sources?", ct))
                        {
                            selected = catalog.AddSources(selected.Definition.Id.ToString(), sources);
                        }
                        break;
                    case WorkspaceAction.RemoveSources:
                        if (selected!.Definition.Sources.Length == 0)
                        {
                            break;
                        }
                        var removed = await interactions.SelectSourcesToRemove(selected.Definition.Sources, ct);
                        if (removed.Count > 0 && await interactions.Confirm($"Remove {removed.Count} source(s) from this workspace?", ct))
                        {
                            selected = catalog.RemoveSources(selected.Definition.Id.ToString(), removed);
                        }
                        break;
                    case WorkspaceAction.Merge:
                        var choices = catalog.List().Where(item => item.Definition.RepositoryRoot == selected!.Definition.RepositoryRoot).ToArray();
                        if (choices.Length < 2)
                        {
                            throw new InvalidOperationException("Create another workspace in this repository before merging.");
                        }
                        var merged = await interactions.SelectWorkspacesToMerge(choices, ct);
                        if (merged.Count < 2)
                        {
                            break;
                        }
                        var mergedName = await interactions.ReadName(null, ct);
                        interactions.ShowConfiguration(mergedName, selected!.Definition.RepositoryRoot, merged.SelectMany(item => item.Definition.Sources).Distinct().ToArray());
                        if (await interactions.Confirm("Create this merged workspace?", ct))
                        {
                            selected = catalog.Merge(mergedName, merged.Select(item => item.Definition.Id.ToString()));
                        }
                        break;
                    case WorkspaceAction.Analyze:
                    case WorkspaceAction.Watch:
                        // A fresh definition catches edits made by another process while the manager is open.
                        selected = catalog.ResolveById(selected!.Definition.Id);
                        await analyze(selected, action == WorkspaceAction.Watch, ct);
                        break;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or KeyNotFoundException)
            {
                interactions.ShowError(ex.Message);
                if (selected is not null)
                {
                    selected = catalog.List().FirstOrDefault(item => item.Definition.Id == selected.Definition.Id);
                }
            }
        }
    }
}
