using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class UpdateWorkspaceEndpoint
{
    public static IEndpointRouteBuilder MapUpdateWorkspaceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{workspaceId:guid}", UpdateWorkspace)
            .WithName(nameof(UpdateWorkspace))
            .Produces<WorkspaceSummary>()
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409);

        return builder;
    }

    private static WorkspaceSummary UpdateWorkspace(
        Guid workspaceId,
        UpdateWorkspaceRequest request,
        IWorkspaceCatalog catalog,
        WorkspaceIndexingCoordinator coordinator)
    {
        catalog.ResolveById(workspaceId);

        return coordinator.UpdateWhileIdle(
            workspaceId,
            () => WorkspaceSummaryMapper.Map(catalog.Update(
                workspaceId.ToString(),
                request.Name,
                request.Sources)));
    }
}
