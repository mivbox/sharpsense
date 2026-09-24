using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Analyze;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Workspaces;

internal abstract class WorkspaceCatalogCommand<TSettings> : AbstractAsyncCommand<TSettings>
    where TSettings : GlobalSettings
{
    protected override void Configure(TSettings settings, IServiceCollection services)
    {
        services.AddFileSystem();
        services.TryAddSingleton<WorkspaceSourceDiscovery>();
        services.TryAddSingleton<IAnalyzeInteractions, SpectreAnalyzeInteractions>();
        services.TryAddSingleton<WorkspaceManagement>();
        services.TryAddSingleton(provider => new WorkspaceCatalog(provider.GetRequiredService<IFileSystem>()));
    }
}

public class WorkspaceSourceSettings : GlobalSettings
{
    [CommandOption("--csharp <path>")]
    public string[] CSharp { get; init; } = [];

    [CommandOption("--typescript <path>")]
    public string[] TypeScript { get; init; } = [];

    [CommandOption("--markdown <path-or-glob>")]
    public string[] Markdown { get; init; } = [];

    [CommandOption("--json")]
    public bool Json { get; init; }

    public WorkspaceSource[] GetSources(string? baseDirectory = null)
        => CSharp.Select(path => new WorkspaceSource(WorkspaceSourceKind.CSharp, Resolve(path, baseDirectory)))
            .Concat(TypeScript.Select(path => new WorkspaceSource(WorkspaceSourceKind.TypeScript, Resolve(path, baseDirectory))))
            .Concat(Markdown.Select(path => new WorkspaceSource(WorkspaceSourceKind.Markdown, Resolve(path, baseDirectory))))
            .ToArray();

    private static string Resolve(string path, string? baseDirectory)
        => baseDirectory is null ? path : Path.GetFullPath(path, baseDirectory);
}

internal static class WorkspaceOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public static void Write(CommandContext context, WorkspaceSelection selection, bool json, string verb)
    {
        if (json)
        {
            CommandOutput.Write(context, JsonSerializer.Serialize(Describe(selection), JsonOptions) + Environment.NewLine);
            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"{verb} workspace '{selection.Definition.Name}' ({selection.Definition.Id}).");
        text.AppendLine($"Repository: {selection.Definition.RepositoryRoot}");
        text.AppendLine($"Configuration: {selection.ConfigurationPath}");
        foreach (var source in selection.Definition.Sources)
        {
            text.AppendLine($"  {source.Kind}: {source.Path}");
        }
        text.AppendLine($"Index with: sharpsense analyze --workspace {selection.Definition.Id}");
        CommandOutput.Write(context, text.ToString());
    }

    public static void WriteList(CommandContext context, IReadOnlyList<WorkspaceSelection> selections, bool json)
    {
        if (json)
        {
            CommandOutput.Write(context, JsonSerializer.Serialize(selections.Select(Describe), JsonOptions) + Environment.NewLine);
            return;
        }

        if (selections.Count == 0)
        {
            CommandOutput.Write(context, "No workspaces registered. Run 'sharpsense configure' to create one." + Environment.NewLine);
            return;
        }

        foreach (var selection in selections)
        {
            CommandOutput.Write(context, $"{selection.Definition.Name} ({selection.Definition.Id}){Environment.NewLine}" +
                $"  {selection.Definition.RepositoryRoot} — {selection.Definition.Sources.Length} source(s){Environment.NewLine}");
        }
    }

    private static object Describe(WorkspaceSelection selection) => new
    {
        selection.Definition.Id,
        selection.Definition.Name,
        selection.Definition.RepositoryRoot,
        selection.Definition.Sources,
        selection.ConfigurationPath,
        selection.Workspace.DatabasePath
    };
}

internal sealed class WorkspaceCreateCommand : WorkspaceCatalogCommand<WorkspaceCreateCommand.Settings>
{
    public sealed class Settings : WorkspaceSourceSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = string.Empty;

        public override ValidationResult Validate() => GetSources().Length == 0
            ? ValidationResult.Error("Add at least one --csharp, --typescript, or --markdown source.")
            : ValidationResult.Success();
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var root = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);
        var selection = host.Services.GetRequiredService<WorkspaceCatalog>()
            .Create(settings.Name, root, settings.GetSources(root));
        WorkspaceOutput.Write(context, selection, settings.Json, "Created");
        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceListCommand : WorkspaceCatalogCommand<WorkspaceListCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--json")]
        public bool Json { get; init; }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        WorkspaceOutput.WriteList(context, host.Services.GetRequiredService<WorkspaceCatalog>().List(), settings.Json);
        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceShowCommand : WorkspaceCatalogCommand<WorkspaceShowCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[name-or-id]")]
        public string? Name { get; init; }

        [CommandOption("--json")]
        public bool Json { get; init; }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<WorkspaceCatalog>().Resolve(
            settings.Name ?? settings.Workspace,
            CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot));
        WorkspaceOutput.Write(context, selection, settings.Json, "Selected");
        return Task.FromResult(0);
    }
}

public sealed class WorkspaceMutationSettings : WorkspaceSourceSettings
{
    [CommandArgument(0, "<name-or-id>")]
    public string Name { get; init; } = string.Empty;

    public override ValidationResult Validate() => GetSources().Length == 0
        ? ValidationResult.Error("Specify at least one --csharp, --typescript, or --markdown source.")
        : ValidationResult.Success();
}

internal sealed class WorkspaceAddCommand : WorkspaceCatalogCommand<WorkspaceMutationSettings>
{
    protected override Task<int> Execute(CommandContext context, WorkspaceMutationSettings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<WorkspaceCatalog>().AddSources(settings.Name, settings.GetSources());
        WorkspaceOutput.Write(context, selection, settings.Json, "Updated");
        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceRemoveCommand : WorkspaceCatalogCommand<WorkspaceMutationSettings>
{
    protected override Task<int> Execute(CommandContext context, WorkspaceMutationSettings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<WorkspaceCatalog>().RemoveSources(settings.Name, settings.GetSources());
        WorkspaceOutput.Write(context, selection, settings.Json, "Updated");
        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceMergeCommand : WorkspaceCatalogCommand<WorkspaceMergeCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name { get; init; } = string.Empty;

        [CommandArgument(1, "<workspaces>")]
        public string[] Workspaces { get; init; } = [];

        [CommandOption("--json")]
        public bool Json { get; init; }

        public override ValidationResult Validate() => Workspaces.Length < 2
            ? ValidationResult.Error("Select at least two existing workspaces to merge.")
            : ValidationResult.Success();
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<WorkspaceCatalog>().Merge(settings.Name, settings.Workspaces);
        WorkspaceOutput.Write(context, selection, settings.Json, "Created");
        return Task.FromResult(0);
    }
}

internal sealed class ConfigureCommand : WorkspaceCatalogCommand<ConfigureCommand.Settings>
{
    public sealed class Settings : WorkspaceSourceSettings
    {
        [CommandArgument(0, "[name]")]
        public string? Name { get; init; }
    }

    protected override async Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var root = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);
        var name = settings.Name ?? settings.Workspace;
        var sources = settings.GetSources(root);
        // Fully specified commands, including JSON mode, remain suitable for automation.
        var selection = !string.IsNullOrWhiteSpace(name) && sources.Length > 0
            ? host.Services.GetRequiredService<WorkspaceCatalog>().Create(name, root, sources)
            : settings.Json
                ? throw new InvalidOperationException("JSON mode requires a workspace name and explicit sources.")
                : await host.Services.GetRequiredService<WorkspaceManagement>().Create(name, root, sources, ct);
        if (selection is not null) WorkspaceOutput.Write(context, selection, settings.Json, "Created");
        return 0;
    }
}

internal sealed class WorkspaceRenameCommand : WorkspaceCatalogCommand<WorkspaceRenameCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<name-or-id>")]
        public string Name { get; init; } = string.Empty;

        [CommandArgument(1, "<new-name>")]
        public string NewName { get; init; } = string.Empty;

        [CommandOption("--json")]
        public bool Json { get; init; }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var catalog = host.Services.GetRequiredService<WorkspaceCatalog>();
        var selection = catalog.Resolve(settings.Name, CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot));
        var updated = catalog.Rename(selection.Definition.Id.ToString(), settings.NewName);
        WorkspaceOutput.Write(context, updated, settings.Json, "Renamed");
        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceManagerCommand : WorkspaceCatalogCommand<WorkspaceManagerCommand.Settings>
{
    public sealed class Settings : GlobalSettings;

    protected override void Configure(Settings settings, IServiceCollection services)
    {
        base.Configure(settings, services);
        services.AddAnalyzeExecution();
    }

    protected override async Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var runner = host.Services.GetRequiredService<WorkspaceAnalysisRunner>();
        await host.Services.GetRequiredService<WorkspaceManagement>().Manage(settings.Workspace,
            CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot),
            (selection, watch, token) => runner.Run(selection, new AnalyzeCommand.Settings { Watch = watch }, token), ct);
        return 0;
    }
}
