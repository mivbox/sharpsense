using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Indexing;

internal static class StopWorkspaceIndexingEndpoint
{
    public static IEndpointRouteBuilder MapStopWorkspaceIndexingEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapDelete("", StopWorkspaceIndexing)
            .WithName(nameof(StopWorkspaceIndexing));

        return builder;
    }

    private static async Task<Ok<WorkspaceIndexingStatus>> StopWorkspaceIndexing(
        Guid workspaceId,
        IWorkspaceCatalog catalog,
        WorkspaceIndexingCoordinator coordinator,
        CancellationToken ct)
    {
        _ = catalog.ResolveById(workspaceId);

        return TypedResults.Ok(await coordinator.Stop(workspaceId, ct));
    }
}
