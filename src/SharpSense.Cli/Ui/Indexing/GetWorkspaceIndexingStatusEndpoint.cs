using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Indexing;

internal static class GetWorkspaceIndexingStatusEndpoint
{
    public static IEndpointRouteBuilder MapGetWorkspaceIndexingStatusEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetWorkspaceIndexingStatus)
            .WithName(nameof(GetWorkspaceIndexingStatus));

        return builder;
    }

    private static Ok<WorkspaceIndexingStatus> GetWorkspaceIndexingStatus(
        Guid workspaceId,
        IWorkspaceCatalog catalog,
        WorkspaceIndexingCoordinator coordinator)
    {
        _ = catalog.ResolveById(workspaceId);

        return TypedResults.Ok(coordinator.GetStatus(workspaceId));
    }
}
