using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text;
using System.Text.Json;

namespace SharpSense.Cli.Workspaces;

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
            CommandOutput.Write(
                context,
                JsonSerializer.Serialize(
                    Describe(selection),
                    _jsonOptions) + Environment.NewLine);

            return;
        }

        var text = new StringBuilder();
        text.AppendLine($"{verb} workspace '{selection.Definition.Name}' ({selection.Definition.Id}).");
        text.AppendLine($"Workspace root: {selection.Definition.WorkspaceRoot}");
        text.AppendLine($"Configuration: {selection.ConfigurationPath}");
        foreach (var source in selection.Definition.Sources)
        {
            text.AppendLine($"  {source.Kind}: {source.Path}");
        }
        text.AppendLine($"Index with: sharpsense analyze --workspace {selection.Definition.Id}");
        CommandOutput.Write(context, text.ToString());
    }

    public static void WriteList(
        CommandContext context,
        IReadOnlyList<WorkspaceSelection> selections,
        bool json,
        Guid? defaultWorkspaceId)
    {
        if (json)
        {
            CommandOutput.Write(
                context,
                JsonSerializer.Serialize(
                    selections
                        .Select(selection => Describe(
                            selection,
                            isDefault: selection.Definition.Id == defaultWorkspaceId)),
                    _jsonOptions) + Environment.NewLine);

            return;
        }

        if (selections.Count == 0)
        {
            CommandOutput.Write(
                context,
                "No workspaces registered. Run 'sharpsense workspace create' to create one." + Environment.NewLine);

            return;
        }

        foreach (var selection in selections)
        {
            var marker = selection.Definition.Id == defaultWorkspaceId ? " (default)" : string.Empty;
            CommandOutput.Write(
                context,
                $"{selection.Definition.Name} ({selection.Definition.Id}){marker}{Environment.NewLine}" +
                $"  {selection.Definition.WorkspaceRoot} — {selection.Definition.Sources.Length} source(s){Environment.NewLine}");
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

    private static object Describe(
        WorkspaceSelection selection,
        WorkspaceSourceRemoval? removal = null,
        bool? isDefault = null) => new
        {
            selection.Definition.Id,
            selection.Definition.Name,
            RepositoryRoot = selection.Definition.WorkspaceRoot,
            selection.Definition.Sources,
            selection.ConfigurationPath,
            selection.Workspace.DatabasePath,
            RemovedSources = removal?.RemovedSources,
            UnmatchedSources = removal?.UnmatchedSources,
            IsDefault = isDefault
        };
}
