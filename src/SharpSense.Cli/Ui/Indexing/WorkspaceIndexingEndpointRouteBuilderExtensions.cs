using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SharpSense.Cli.Ui.Indexing;

internal static class WorkspaceIndexingEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceIndexingEndpoints(this IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/api/workspaces/{workspaceId:guid}/indexing")
            .WithTags("Workspace indexing")
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(500);

        group.MapGetWorkspaceIndexingStatusEndpoint()
            .MapStreamWorkspaceIndexingStatusEndpoint()
            .MapStartWorkspaceIndexingEndpoint()
            .MapStopWorkspaceIndexingEndpoint();

        return builder;
    }
}
