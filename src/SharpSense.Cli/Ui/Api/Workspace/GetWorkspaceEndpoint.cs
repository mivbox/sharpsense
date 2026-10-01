using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class GetWorkspaceEndpoint
{
    public static IEndpointRouteBuilder MapGetWorkspaceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{workspaceId:guid}", GetWorkspace)
            .WithName(nameof(GetWorkspace))
            .Produces<WorkspaceSummary>()
            .ProducesProblem(404);

        return builder;
    }

    private static WorkspaceSummary GetWorkspace(
        Guid workspaceId,
        IWorkspaceCatalog catalog) => WorkspaceSummaryMapper.Map(catalog.ResolveById(workspaceId));
}
