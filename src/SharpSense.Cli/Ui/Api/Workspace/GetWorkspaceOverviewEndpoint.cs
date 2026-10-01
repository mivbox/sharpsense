using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.WorkspaceExplorer.Abstractions;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

internal static class GetWorkspaceOverviewEndpoint
{
    public static IEndpointRouteBuilder MapGetWorkspaceOverviewEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/overview", GetWorkspaceOverview)
            .WithName(nameof(GetWorkspaceOverview))
            .Produces<WorkspaceOverview>();

        return builder;
    }

    private static async Task<WorkspaceOverview> GetWorkspaceOverview(
        IRepositoryWorkspace workspace,
        IWorkspaceOverviewReader reader,
        CancellationToken ct)
    {
        var counts = await reader.Read(ct);
        var sources = workspace.Definition?.Sources
            .Select(source => new WorkspaceSourceOverview(
                source.Kind.ToString(),
                source.Path))
            .ToArray() ?? [];

        return new WorkspaceOverview(
            workspace.WorkspaceName ?? Path.GetFileName(workspace.RootPath.TrimEnd(Path.DirectorySeparatorChar)),
            workspace.RootPath,
            counts.Nodes > 0,
            counts.Projects,
            counts.Documents,
            counts.Nodes,
            counts.Edges,
            counts.Memories,
            counts.Directories,
            workspace.WorkspaceId,
            sources);
    }
}
