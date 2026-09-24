using SharpSense.Application.Indexing;
using SharpSense.Cli.Workspaces;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;

namespace SharpSense.Cli.Analyze;

internal sealed class SpectreAnalyzeInteractions(IAnsiConsole console, WorkspaceSourceDiscovery discovery) : IAnalyzeInteractions
{
    public bool IsInteractive => console.Profile.Capabilities.Interactive;

    public async Task<WorkspaceSelection?> SelectWorkspace(IReadOnlyList<WorkspaceSelection> choices, CancellationToken ct)
    {
        var items = choices.Select(static selection => new WorkspaceChoice(selection)).Append(new WorkspaceChoice(null));
        var choice = await console.PromptAsync(new SelectionPrompt<WorkspaceChoice>()
            .Title("Select a workspace")
            .UseConverter(static item => item.Selection is { } selection
                ? Markup.Escape($"{selection.Definition.Name} — {selection.Definition.RepositoryRoot}")
                : "Create a workspace")
            .AddChoices(items), ct);
        return choice.Selection;
    }

    public Task<WorkspaceAction> SelectAction(WorkspaceSelection? selection, CancellationToken ct)
    {
        var actions = selection is null
            ? new[] { WorkspaceAction.Select, WorkspaceAction.Create, WorkspaceAction.Exit }
            : Enum.GetValues<WorkspaceAction>();
        return console.PromptAsync(new SelectionPrompt<WorkspaceAction>()
            .Title(selection is null ? "Workspace manager" : $"Workspace: [bold]{Markup.Escape(selection.Definition.Name)}[/]")
            .UseConverter(static action => action switch
            {
                WorkspaceAction.Select => "Select workspace",
                WorkspaceAction.Create => "Create workspace",
                WorkspaceAction.AddSources => "Add sources",
                WorkspaceAction.RemoveSources => "Remove sources",
                WorkspaceAction.Watch => "Analyze and watch",
                _ => action.ToString()
            })
            .AddChoices(actions), ct);
    }

    public Task<string> ReadName(string? current, CancellationToken ct)
    {
        var prompt = new TextPrompt<string>("Workspace name:");
        if (!string.IsNullOrWhiteSpace(current))
        {
            prompt.DefaultValue(current);
        }
        return console.PromptAsync(prompt, ct);
    }

    public Task<string> ReadRepositoryRoot(string current, CancellationToken ct) =>
        console.PromptAsync(new TextPrompt<string>("Repository directory:").DefaultValue(current), ct);

    public async Task<IReadOnlyList<WorkspaceSource>> SelectSources(string root, CancellationToken ct)
    {
        var selected = new List<WorkspaceSource>();
        while (true)
        {
            var action = await console.PromptAsync(new SelectionPrompt<string>()
                .Title($"Sources selected: {selected.Count}")
                .AddChoices("Discover sources", "Enter source manually", "Finish source selection"), ct);
            if (action == "Finish source selection")
            {
                if (selected.Count > 0)
                {
                    return selected;
                }
                ShowError("Select at least one source.");
                continue;
            }

            if (action == "Discover sources")
            {
                try
                {
                    var discovered = discovery.Discover(root, ct);
                    if (discovered.Sources.Count == 0)
                    {
                        console.WriteLine("No sources discovered. Enter an explicit path or glob.");
                        continue;
                    }
                    var sources = await console.PromptAsync(new MultiSelectionPrompt<WorkspaceSource>()
                        .Title("Choose sources (space to select, enter to continue)")
                        .NotRequired()
                        .UseConverter(DescribeSource)
                        .AddChoices(discovered.Sources), ct);
                    selected.AddRange(sources.Select(source => source with { Path = Path.GetFullPath(source.Path, root) }));
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    ShowError(ex.Message);
                    console.WriteLine("You can still enter explicit paths or globs.");
                }
            }
            else
            {
                var kind = await console.PromptAsync(new SelectionPrompt<WorkspaceSourceKind>()
                    .Title("Source language:").AddChoices(Enum.GetValues<WorkspaceSourceKind>()), ct);
                var label = kind switch
                {
                    WorkspaceSourceKind.CSharp => "Solution or project path (.sln, .slnx, .csproj):",
                    WorkspaceSourceKind.TypeScript => "TypeScript project path (tsconfig.json or directory):",
                    _ => "Markdown file, directory, or glob:"
                };
                var path = await console.PromptAsync(new TextPrompt<string>(label), ct);
                selected.Add(new WorkspaceSource(kind, Path.GetFullPath(path, root)));
            }
            selected = selected.Distinct().ToList();
        }
    }

    public async Task<IReadOnlyList<WorkspaceSource>> SelectSourcesToRemove(IReadOnlyList<WorkspaceSource> sources, CancellationToken ct) =>
        await console.PromptAsync(new MultiSelectionPrompt<WorkspaceSource>()
            .Title("Select sources to remove").NotRequired().UseConverter(DescribeSource).AddChoices(sources), ct);

    public async Task<IReadOnlyList<WorkspaceSelection>> SelectWorkspacesToMerge(IReadOnlyList<WorkspaceSelection> choices, CancellationToken ct) =>
        await console.PromptAsync(new MultiSelectionPrompt<WorkspaceSelection>()
            .Title("Select at least two workspaces from the same repository")
            .NotRequired().UseConverter(static selection => Markup.Escape(selection.Definition.Name)).AddChoices(choices), ct);

    public Task<bool> Confirm(string message, CancellationToken ct) =>
        console.PromptAsync(new ConfirmationPrompt(Markup.Escape(message)) { DefaultValue = false }, ct);

    public void ShowWorkspace(WorkspaceSelection selection)
    {
        ShowConfiguration(selection.Definition.Name, selection.Definition.RepositoryRoot, selection.Definition.Sources);
        console.WriteLine($"Configuration: {selection.ConfigurationPath}");
    }

    public void ShowConfiguration(string name, string root, IReadOnlyList<WorkspaceSource> sources)
    {
        var table = new Table().AddColumn("Language").AddColumn("Source");
        foreach (var source in sources)
        {
            table.AddRow(source.Kind.ToString(), Markup.Escape(source.Path));
        }
        console.Write(new Panel(table).Header(Markup.Escape(name)));
        console.WriteLine($"Repository: {root}");
    }

    public void ShowError(string message) => console.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");

    private static string DescribeSource(WorkspaceSource source) => Markup.Escape($"{source.Kind}: {source.Path}");
    private sealed record WorkspaceChoice(WorkspaceSelection? Selection);
}
