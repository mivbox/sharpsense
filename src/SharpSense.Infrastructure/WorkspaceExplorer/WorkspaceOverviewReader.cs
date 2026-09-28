using Microsoft.EntityFrameworkCore;
using SharpSense.Application.WorkspaceExplorer.Abstractions;
using SharpSense.Application.WorkspaceExplorer.Models;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.WorkspaceExplorer;

internal sealed class WorkspaceOverviewReader(IDbContextFactory<SharpSenseDbContext> factory) : IWorkspaceOverviewReader
{
    public async Task<WorkspaceOverviewCounts> Read(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);

        return new WorkspaceOverviewCounts(
            await db.ProjectNodes.CountAsync(ct),
            await db.Documents.CountAsync(ct),
            await db.CodeNodes.CountAsync(ct),
            await db.DependencyEdges.CountAsync(ct),
            await db.MemoryNodes.CountAsync(ct),
            await db.Directories.CountAsync(ct));
    }
}
