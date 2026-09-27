using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace SharpSense.Cli.Workspaces;

internal abstract class WorkspaceCatalogCommand<TSettings> : AbstractAsyncCommand<TSettings>
    where TSettings : CliSettings
{
    protected override void Configure(TSettings settings, IServiceCollection services)
    {
        services.AddFileSystem();
        services.TryAddSingleton<WorkspaceSourceDiscovery>();
        services.TryAddSingleton<IWorkspaceInteractions, SpectreWorkspaceInteractions>();
        services.TryAddSingleton<WorkspaceSetup>();
        services.AddWorkspaceCatalog();
    }
}

internal class WorkspaceSourceSettings : CliSettings
{
    [CommandOption("--repo-root <path>")]
    [Description("Resolve source paths against this directory. Defaults to the current directory.")]
    public string? RepositoryRoot
    {
        get; init;
    }

    [CommandOption("--csharp <path>")]
    [Description("A C# project or solution path. May be repeated.")]
    public string[] CSharp
    {
        get; init;
    } = [];

    [CommandOption("--typescript <path>")]
    [Description("A TypeScript configuration or directory path. May be repeated.")]
    public string[] TypeScript
    {
        get; init;
    } = [];

    [CommandOption("--markdown <path-or-glob>")]
    [Description("A Markdown path or quoted glob. May be repeated.")]
    public string[] Markdown
    {
        get; init;
    } = [];

    [CommandOption("--json")]
    [Description("Write machine-readable JSON without interactive prompts.")]
    public bool Json
    {
        get; init;
    }

    public bool HasSources => CSharp.Length + TypeScript.Length + Markdown.Length > 0;

    public WorkspaceSource[] GetSources(string baseDirectory)
        => CSharp.Select(path => new WorkspaceSource(WorkspaceSourceKind.CSharp, Path.GetFullPath(path, baseDirectory)))
            .Concat(TypeScript.Select(path => new WorkspaceSource(WorkspaceSourceKind.TypeScript, Path.GetFullPath(path, baseDirectory))))
            .Concat(Markdown.Select(path => new WorkspaceSource(WorkspaceSourceKind.Markdown, Path.GetFullPath(path, baseDirectory))))
            .ToArray();
}

internal static class WorkspaceOutput
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter()
        }
    };

    public static void Write(CommandContext context, WorkspaceSelection selection, bool json, string verb)
    {
        if (json)
        {
            CommandOutput.Write(context, JsonSerializer.Serialize(Describe(selection), _jsonOptions) + Environment.NewLine);

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

    public static void WriteList(CommandContext context, IReadOnlyList<WorkspaceSelection> selections, bool json, Guid? defaultWorkspaceId)
    {
        if (json)
        {
            CommandOutput.Write(
                context,
                JsonSerializer.Serialize(
                    selections.Select(selection => Describe(selection, isDefault: selection.Definition.Id == defaultWorkspaceId)),
                    _jsonOptions) + Environment.NewLine);

            return;
        }

        if (selections.Count == 0)
        {
            CommandOutput.Write(context, "No workspaces registered. Run 'sharpsense workspace create' to create one." + Environment.NewLine);

            return;
        }

        foreach (var selection in selections)
        {
            var marker = selection.Definition.Id == defaultWorkspaceId ? " (default)" : string.Empty;
            CommandOutput.Write(
                context,
                $"{selection.Definition.Name} ({selection.Definition.Id}){marker}{Environment.NewLine}" +
                $"  {selection.Definition.RepositoryRoot} — {selection.Definition.Sources.Length} source(s){Environment.NewLine}");
        }
    }

    public static void WriteRemoval(CommandContext context, WorkspaceSourceRemoval removal, bool json)
    {
        var output = json
            ? JsonSerializer.Serialize(Describe(removal.Selection, removal), _jsonOptions) + Environment.NewLine
            : FormatRemoval(removal);
        CommandOutput.Write(context, output);
    }

    private static string FormatRemoval(WorkspaceSourceRemoval removal)
    {
        var text = new StringBuilder();
        text.AppendLine($"Removed {removal.RemovedSources.Count} source(s) from workspace '{removal.Selection.Definition.Name}'.");
        foreach (var source in removal.RemovedSources)
        {
            text.AppendLine($"  Removed {source.Kind}: {source.Path}");
        }
        foreach (var source in removal.UnmatchedSources)
        {
            text.AppendLine($"  Not registered {source.Kind}: {source.Path}");
        }

        return text.ToString();
    }

    private static object Describe(WorkspaceSelection selection, WorkspaceSourceRemoval? removal = null, bool? isDefault = null) => new
    {
        selection.Definition.Id,
        selection.Definition.Name,
        selection.Definition.RepositoryRoot,
        selection.Definition.Sources,
        selection.ConfigurationPath,
        selection.Workspace.DatabasePath,
        RemovedSources = removal?.RemovedSources,
        UnmatchedSources = removal?.UnmatchedSources,
        IsDefault = isDefault
    };
}

internal sealed class WorkspaceCreateCommand : WorkspaceCatalogCommand<WorkspaceCreateCommand.Settings>
{
    public sealed class Settings : WorkspaceSourceSettings
    {
        [CommandArgument(0, "[name]")]
        [Description("Name for the new workspace. Prompts when omitted in an interactive terminal.")]
        public string? Name
        {
            get; init;
        }
    }

    protected override async Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var root = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);
        var sources = settings.GetSources(root);
        if (settings.Json && (string.IsNullOrWhiteSpace(settings.Name) || sources.Length == 0))
        {
            throw new InvalidOperationException("JSON mode requires a workspace name and explicit sources.");
        }

        var selection = await host.Services.GetRequiredService<WorkspaceSetup>()
            .Create(settings.Name, root, sources, ct);
        if (selection is not null)
        {
            WorkspaceOutput.Write(context, selection, settings.Json, "Created");
        }

        return 0;
    }
}

internal sealed class WorkspaceListCommand : WorkspaceCatalogCommand<WorkspaceListCommand.Settings>
{
    public sealed class Settings : CliSettings
    {
        [CommandOption("--json")]
        [Description("Write machine-readable JSON without interactive prompts.")]
        public bool Json
        {
            get; init;
        }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var catalog = host.Services.GetRequiredService<IWorkspaceCatalog>();
        Guid? defaultWorkspaceId = null;
        try
        {
            defaultWorkspaceId = catalog.GetDefaultWorkspaceId();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                "Warning: Cannot read the saved default workspace. Run 'sharpsense workspace use <name-or-id>' to replace it.");
        }

        WorkspaceOutput.WriteList(context, catalog.List(), settings.Json, defaultWorkspaceId);

        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceUseCommand : WorkspaceCatalogCommand<WorkspaceUseCommand.Settings>
{
    public sealed class Settings : CliSettings
    {
        [CommandArgument(0, "<name-or-id>")]
        [Description("Workspace to use by default from any directory.")]
        public string Name
        {
            get; init;
        } = string.Empty;

        [CommandOption("--json")]
        [Description("Write the selected workspace as JSON.")]
        public bool Json
        {
            get; init;
        }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .Use(settings.Name);
        WorkspaceOutput.Write(context, selection, settings.Json, "Selected default");

        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceShowCommand : WorkspaceCatalogCommand<WorkspaceShowCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[name-or-id]")]
        public string? Name
        {
            get; init;
        }

        [CommandOption("--json")]
        [Description("Write machine-readable JSON without interactive prompts.")]
        public bool Json
        {
            get; init;
        }

        public override ValidationResult Validate()
        {
            if (Name is not null && Workspace is not null)
            {
                return ValidationResult.Error("Specify either a workspace argument or --workspace, not both.");
            }

            return Name is not null && string.IsNullOrWhiteSpace(Name)
                ? ValidationResult.Error("Workspace name or ID must not be empty or whitespace.")
                : base.Validate();
        }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .Resolve(
            settings.Name ?? settings.Workspace,
            CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot));
        WorkspaceOutput.Write(context, selection, settings.Json, "Selected");

        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceMutationSettings : WorkspaceSourceSettings
{
    [CommandArgument(0, "<name-or-id>")]
    public string Name
    {
        get; init;
    } = string.Empty;

    public override ValidationResult Validate() => !HasSources
        ? ValidationResult.Error("Specify at least one --csharp, --typescript, or --markdown source.")
        : ValidationResult.Success();
}

internal sealed class WorkspaceAddCommand : WorkspaceCatalogCommand<WorkspaceMutationSettings>
{
    protected override Task<int> Execute(CommandContext context, WorkspaceMutationSettings settings, IHost host, CancellationToken ct)
    {
        var sourceBase = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);
        var selection = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .AddSources(settings.Name, settings.GetSources(sourceBase));
        WorkspaceOutput.Write(context, selection, settings.Json, "Updated");

        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceRemoveCommand : WorkspaceCatalogCommand<WorkspaceMutationSettings>
{
    protected override Task<int> Execute(CommandContext context, WorkspaceMutationSettings settings, IHost host, CancellationToken ct)
    {
        var sourceBase = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);
        var removal = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .RemoveSources(settings.Name, settings.GetSources(sourceBase));
        WorkspaceOutput.WriteRemoval(context, removal, settings.Json);

        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceMergeCommand : WorkspaceCatalogCommand<WorkspaceMergeCommand.Settings>
{
    public sealed class Settings : CliSettings
    {
        [CommandArgument(0, "<name>")]
        public string Name
        {
            get; init;
        } = string.Empty;

        [CommandArgument(1, "<workspaces>")]
        public string[] Workspaces
        {
            get; init;
        } = [];

        [CommandOption("--json")]
        [Description("Write machine-readable JSON without interactive prompts.")]
        public bool Json
        {
            get; init;
        }

        public override ValidationResult Validate() => Workspaces.Length < 2
            ? ValidationResult.Error("Select at least two existing workspaces to merge.")
            : ValidationResult.Success();
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = host.Services.GetRequiredService<IWorkspaceCatalog>()
            .Merge(settings.Name, settings.Workspaces);
        WorkspaceOutput.Write(context, selection, settings.Json, "Created");

        return Task.FromResult(0);
    }
}

internal sealed class WorkspaceRenameCommand : WorkspaceCatalogCommand<WorkspaceRenameCommand.Settings>
{
    public sealed class Settings : CliSettings
    {
        [CommandArgument(0, "<name-or-id>")]
        public string Name
        {
            get; init;
        } = string.Empty;

        [CommandArgument(1, "<new-name>")]
        public string NewName
        {
            get; init;
        } = string.Empty;

        [CommandOption("--json")]
        [Description("Write machine-readable JSON without interactive prompts.")]
        public bool Json
        {
            get; init;
        }
    }

    protected override Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var catalog = host.Services.GetRequiredService<IWorkspaceCatalog>();
        var updated = catalog.Rename(settings.Name, settings.NewName);
        WorkspaceOutput.Write(context, updated, settings.Json, "Renamed");

        return Task.FromResult(0);
    }
}
