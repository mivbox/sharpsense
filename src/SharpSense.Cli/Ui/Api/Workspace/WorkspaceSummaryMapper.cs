using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class WorkspaceSummaryMapper
{
    public static WorkspaceSummary Map(WorkspaceSelection selection) => new(
        selection.Definition.Id,
        selection.Definition.Name,
        selection.Definition.WorkspaceRoot,
        selection.Definition.Sources
            .Select(static source => new WorkspaceSourceOverview(
                source.Kind.ToString(),
                source.Path))
            .ToArray());
}
