using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class ListWorkspacesEndpoint
{
    public static IEndpointRouteBuilder MapListWorkspacesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("", ListWorkspaces)
            .WithName(nameof(ListWorkspaces))
            .Produces<WorkspaceCatalogResponse>();

        return builder;
    }

    private static WorkspaceCatalogResponse ListWorkspaces(IWorkspaceCatalog catalog, WorkspaceUiOptions options)
    {
        var selections = catalog.List();
        Guid? initialId = null;
        if (!string.IsNullOrWhiteSpace(options.InitialWorkspace))
        {
            initialId = selections
                .FirstOrDefault(selection => string.Equals(
                    selection.Definition.Name,
                    options.InitialWorkspace,
                    StringComparison.OrdinalIgnoreCase) || string.Equals(
                        selection.Definition.Id.ToString(),
                        options.InitialWorkspace,
                        StringComparison.OrdinalIgnoreCase))?.Definition.Id;
        }

        var workspaces = selections
            .Select(WorkspaceSummaryMapper.Map)
            .ToArray();

        return new WorkspaceCatalogResponse(workspaces, initialId);
    }
}
