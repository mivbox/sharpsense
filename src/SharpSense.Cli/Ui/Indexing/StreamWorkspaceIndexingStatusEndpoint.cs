using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Indexing;

internal static class StreamWorkspaceIndexingStatusEndpoint
{
    public static IEndpointRouteBuilder MapStreamWorkspaceIndexingStatusEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/events", StreamWorkspaceIndexingStatus)
            .WithName(nameof(StreamWorkspaceIndexingStatus))
            .WithSummary("Stream current workspace indexing status; reconnects start with a complete snapshot.")
            .Produces<WorkspaceIndexingStatus>(StatusCodes.Status200OK, "text/event-stream");

        return builder;
    }

    private static ServerSentEventsResult<WorkspaceIndexingStatus> StreamWorkspaceIndexingStatus(
        Guid workspaceId,
        IWorkspaceCatalog catalog,
        WorkspaceIndexingCoordinator coordinator,
        CancellationToken ct)
    {
        _ = catalog.ResolveById(workspaceId);

        return TypedResults.ServerSentEvents(coordinator.Events(workspaceId, ct));
    }
}
