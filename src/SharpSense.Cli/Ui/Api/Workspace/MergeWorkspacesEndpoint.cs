using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class MergeWorkspacesEndpoint
{
    public static IEndpointRouteBuilder MapMergeWorkspacesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/merge", MergeWorkspaces)
            .WithName(nameof(MergeWorkspaces))
            .Produces<WorkspaceSummary>(201)
            .ProducesProblem(400);

        return builder;
    }

    private static IResult MergeWorkspaces(MergeWorkspacesRequest request, IWorkspaceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(request.WorkspaceIds);
        var selection = catalog.Merge(request.Name, request.WorkspaceIds.Select(static id => id.ToString()));

        return Results.Created($"/api/workspaces/{selection.Definition.Id:D}", WorkspaceSummaryMapper.Map(selection));
    }
}
