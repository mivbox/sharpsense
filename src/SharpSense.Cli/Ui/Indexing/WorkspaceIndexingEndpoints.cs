using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Ui.Api;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Indexing;

public static class WorkspaceIndexingEndpoints
{
    public static IServiceCollection AddWorkspaceIndexing(this IServiceCollection services)
    {
        services.AddSingleton<WorkspaceIndexingCoordinator>();
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<WorkspaceIndexingCoordinator>());
        return services;
    }

    public static IEndpointRouteBuilder MapWorkspaceIndexingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces/{workspaceId:guid}/indexing").WithTags("Workspace indexing");
        group.MapGet("", (Guid workspaceId, WorkspaceCatalog catalog, WorkspaceIndexingCoordinator coordinator) =>
        {
            _ = WorkspaceCatalogEndpoints.Resolve(catalog, workspaceId);
            return TypedResults.Ok(coordinator.GetStatus(workspaceId));
        }).WithName("GetWorkspaceIndexingStatus");

        group.MapPost("", (Guid workspaceId, StartWorkspaceIndexingRequest request, WorkspaceCatalog catalog, WorkspaceIndexingCoordinator coordinator) =>
        {
            var status = coordinator.Start(
                () => WorkspaceCatalogEndpoints.Resolve(catalog, workspaceId), request);
            return TypedResults.Accepted($"/api/workspaces/{workspaceId}/indexing", status);
        }).WithName("StartWorkspaceIndexing");

        group.MapDelete("", async (Guid workspaceId, WorkspaceCatalog catalog, WorkspaceIndexingCoordinator coordinator, CancellationToken ct) =>
        {
            _ = WorkspaceCatalogEndpoints.Resolve(catalog, workspaceId);
            return TypedResults.Ok(await coordinator.Stop(workspaceId, ct));
        }).WithName("StopWorkspaceIndexing");
        return endpoints;
    }
}
