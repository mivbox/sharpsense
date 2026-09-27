using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.WorkspaceExplorer.Abstractions;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree.Models;
using SharpSense.Application.WorkspaceExplorer.Models;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Api;

public sealed record WorkspaceOverview(
    string Name,
    string RepositoryRoot,
    bool Indexed,
    int ProjectCount,
    int DocumentCount,
    int NodeCount,
    int EdgeCount,
    int MemoryCount,
    int DirectoryCount,
    Guid? WorkspaceId = null,
    IReadOnlyList<WorkspaceSourceOverview>? Sources = null);

public sealed record WorkspaceSourceOverview(string Kind, string Path);

internal static class WorkspaceEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet(
            "/api/tree",
            async (string? path, IQueryHandler<GetWorkspaceTreeQuery, WorkspaceTreeResult> handler, CancellationToken ct) =>
                await handler.Handle(new GetWorkspaceTreeQuery(string.IsNullOrWhiteSpace(path) ? "/" : path), ct))
            .WithName("GetWorkspaceTree")
            .WithTags("Workspace")
            .Produces<WorkspaceTreeResult>()
            .ProducesProblem(400);

        app.MapGet(
            "/api/overview",
            async (IRepositoryWorkspace workspace, IWorkspaceOverviewReader reader, CancellationToken ct) =>
        {
            var counts = await reader.Read(ct);
            var sources = workspace.Definition?.Sources
                .Select(source => new WorkspaceSourceOverview(source.Kind.ToString(), source.Path))
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
        })
            .WithName("GetWorkspaceOverview")
            .WithTags("Workspace")
            .Produces<WorkspaceOverview>();
    }
}
