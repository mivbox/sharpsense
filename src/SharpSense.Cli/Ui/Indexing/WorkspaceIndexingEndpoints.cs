using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Ui.Api;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Indexing;

internal static class WorkspaceIndexingEndpoints
{
    public static IServiceCollection AddWorkspaceIndexing(this IServiceCollection services)
    {
        services.AddSingleton<WorkspaceIndexingCoordinator>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<WorkspaceIndexingCoordinator>());

        return services;
    }

    public static IEndpointRouteBuilder MapWorkspaceIndexingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/indexing")
            .WithTags("Workspace indexing");
        group.MapGet(
            "",
            (Guid workspaceId, IWorkspaceCatalog catalog, WorkspaceIndexingCoordinator coordinator) =>
        {
            _ = WorkspaceCatalogEndpoints.Resolve(catalog, workspaceId);

            return TypedResults.Ok(coordinator.GetStatus(workspaceId));
        })
            .WithName("GetWorkspaceIndexingStatus");

        group.MapGet(
            "/events",
            (Guid workspaceId, IWorkspaceCatalog catalog, WorkspaceIndexingCoordinator coordinator, CancellationToken ct) =>
        {
            _ = WorkspaceCatalogEndpoints.Resolve(catalog, workspaceId);

            return TypedResults.ServerSentEvents(coordinator.Events(workspaceId, ct));
        })
            .WithName("StreamWorkspaceIndexingStatus")
            .WithSummary("Stream current workspace indexing status; reconnects start with a complete snapshot.")
            .Produces<WorkspaceIndexingStatus>(StatusCodes.Status200OK, "text/event-stream");

        group.MapPost(
            "",
            (Guid workspaceId, StartWorkspaceIndexingRequest request, IWorkspaceCatalog catalog, WorkspaceIndexingCoordinator coordinator) =>
        {
            var status = coordinator.Start(
                () => WorkspaceCatalogEndpoints.Resolve(catalog, workspaceId),
                request);

            return TypedResults.Accepted(
                $"/api/workspaces/{workspaceId}/indexing",
                status);
        })
            .WithName("StartWorkspaceIndexing");

        group.MapDelete(
            "",
            async (Guid workspaceId, IWorkspaceCatalog catalog, WorkspaceIndexingCoordinator coordinator, CancellationToken ct) =>
        {
            _ = WorkspaceCatalogEndpoints.Resolve(catalog, workspaceId);

            return TypedResults.Ok(await coordinator.Stop(workspaceId, ct));
        })
            .WithName("StopWorkspaceIndexing");

        return endpoints;
    }
}
