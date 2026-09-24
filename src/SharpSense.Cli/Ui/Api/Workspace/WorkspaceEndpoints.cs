using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree.Models;
using SharpSense.Application.WorkspaceExplorer.Models;
using SharpSense.Infrastructure.Persistence;
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
        app.MapGet("/api/tree", async (string? path, IQueryHandler<GetWorkspaceTreeQuery, WorkspaceTreeResult> handler, CancellationToken ct) =>
                await handler.Handle(new GetWorkspaceTreeQuery(string.IsNullOrWhiteSpace(path) ? "/" : path), ct))
            .WithName("GetWorkspaceTree").WithTags("Workspace").Produces<WorkspaceTreeResult>().ProducesProblem(400);

        app.MapGet("/api/overview", async (IRepositoryWorkspace workspace, IDbContextFactory<SharpSenseDbContext> factory, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var nodes = await db.CodeNodes.CountAsync(ct);
            var sources = workspace.Definition?.Sources
                .Select(source => new WorkspaceSourceOverview(source.Kind.ToString(), source.Path))
                .ToArray() ?? [];

            return new WorkspaceOverview(
                workspace.WorkspaceName ?? Path.GetFileName(workspace.RootPath.TrimEnd(Path.DirectorySeparatorChar)),
                workspace.RootPath,
                nodes > 0,
                await db.ProjectNodes.CountAsync(ct),
                await db.Documents.CountAsync(ct),
                nodes,
                await db.DependencyEdges.CountAsync(ct),
                await db.MemoryNodes.CountAsync(ct),
                await db.Directories.CountAsync(ct),
                workspace.WorkspaceId,
                sources);
        }).WithName("GetWorkspaceOverview").WithTags("Workspace").Produces<WorkspaceOverview>();
    }
}
