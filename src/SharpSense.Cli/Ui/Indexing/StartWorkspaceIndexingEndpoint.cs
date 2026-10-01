using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Indexing;

internal static class StartWorkspaceIndexingEndpoint
{
    public static IEndpointRouteBuilder MapStartWorkspaceIndexingEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("", StartWorkspaceIndexing)
            .WithName(nameof(StartWorkspaceIndexing));

        return builder;
    }

    private static Accepted<WorkspaceIndexingStatus> StartWorkspaceIndexing(
        Guid workspaceId,
        StartWorkspaceIndexingRequest request,
        IWorkspaceCatalog catalog,
        WorkspaceIndexingCoordinator coordinator)
    {
        var status = coordinator.Start(() => catalog.ResolveById(workspaceId), request);

        return TypedResults.Accepted($"/api/workspaces/{workspaceId}/indexing", status);
    }
}
