using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class CreateWorkspaceEndpoint
{
    public static IEndpointRouteBuilder MapCreateWorkspaceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("", CreateWorkspace)
            .WithName(nameof(CreateWorkspace))
            .Produces<WorkspaceSummary>(201)
            .ProducesProblem(400);

        return builder;
    }

    private static IResult CreateWorkspace(CreateWorkspaceRequest request, IWorkspaceCatalog catalog)
    {
        var selected = catalog.Create(request.Name, request.RepositoryRoot, request.Sources);

        return Results.Created($"/api/workspaces/{selected.Definition.Id:D}", WorkspaceSummaryMapper.Map(selected));
    }
}
