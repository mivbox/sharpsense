using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree.Models;
using SharpSense.Application.WorkspaceExplorer.Models;

namespace SharpSense.Cli.Ui.Api;

internal static class GetWorkspaceTreeEndpoint
{
    public static IEndpointRouteBuilder MapGetWorkspaceTreeEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/tree", GetWorkspaceTree)
            .WithName(nameof(GetWorkspaceTree))
            .Produces<WorkspaceTreeResult>()
            .ProducesProblem(400);

        return builder;
    }

    private static async Task<WorkspaceTreeResult> GetWorkspaceTree(
        string? path,
        IQueryHandler<GetWorkspaceTreeQuery, WorkspaceTreeResult> handler,
        CancellationToken ct) => await handler.Handle(
            new GetWorkspaceTreeQuery(string.IsNullOrWhiteSpace(path) ? "/" : path),
            ct);
}
