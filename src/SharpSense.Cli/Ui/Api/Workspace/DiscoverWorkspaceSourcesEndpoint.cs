using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Cli.Workspaces;

namespace SharpSense.Cli.Ui.Api;

internal static class DiscoverWorkspaceSourcesEndpoint
{
    public static IEndpointRouteBuilder MapDiscoverWorkspaceSourcesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/discover", DiscoverWorkspaceSources)
            .WithName(nameof(DiscoverWorkspaceSources))
            .Produces<WorkspaceDiscoveryResponse>()
            .ProducesProblem(400);

        return builder;
    }

    private static WorkspaceDiscoveryResponse DiscoverWorkspaceSources(
        DiscoverWorkspaceRequest request,
        WorkspaceSourceDiscovery discovery,
        CancellationToken ct)
    {
        var result = discovery.Discover(request.RepositoryRoot, ct);

        return new WorkspaceDiscoveryResponse(
            result.WorkspaceRoot,
            result.Sources
                .Select(static source => new WorkspaceSourceOverview(
                    source.Kind.ToString(),
                    source.Path))
                .ToArray());
    }
}
