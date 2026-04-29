using Microsoft.EntityFrameworkCore;
using SharpSense.Application.WorkspaceExplorer.Abstractions;
using SharpSense.Application.WorkspaceExplorer.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Domain.KnowledgeGraph.Enums;
using PersistedWorkspaceTreeNode = SharpSense.Domain.KnowledgeGraph.Nodes.WorkspaceTreeNode;

namespace SharpSense.Infrastructure.WorkspaceExplorer;

public sealed class WorkspaceTreeRepository(SharpSenseDbContext context)
    : IWorkspaceTreeRepository
{
    private const string RootPath = "/";

    public async Task<WorkspaceTreeResult> GetTree(
        string path,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizePath(path);
        var nodes = await GetChildNodes(normalizedPath)
            .AsNoTracking()
            .OrderBy(
                node => node.Kind == WorkspaceTreeNodeKind.Folder
                    ? 0
                    : node.Kind == WorkspaceTreeNodeKind.Project
                        ? 1
                        : 2)
            .ThenBy(node => node.Label)
            .ThenBy(node => node.Path)
            .Select(
                node => new WorkspaceTreeNode(
                    node.Id,
                    node.ParentId,
                    node.Path,
                    node.Label,
                    node.Kind.ToString().ToLowerInvariant(),
                    node.HasChildren,
                    node.ChildCount,
                    node.IsSelectable))
            .ToArrayAsync(ct);

        return new WorkspaceTreeResult(normalizedPath, nodes);
    }

    private IQueryable<PersistedWorkspaceTreeNode> GetChildNodes(string path)
    {
        if (string.Equals(path, RootPath, StringComparison.Ordinal))
        {
            return context.WorkspaceTreeNodes.Where(node => node.ParentId == null);
        }

        return context.WorkspaceTreeNodes.Where(node => node.ParentId == path);
    }

    private static string NormalizePath(string path)
        => path.Trim();
}
