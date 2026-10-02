using Microsoft.EntityFrameworkCore;
using SharpSense.Application.WorkspaceExplorer.Abstractions;
using SharpSense.Application.WorkspaceExplorer.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.WorkspaceExplorer;

internal sealed class WorkspaceTreeRepository(SharpSenseDbContext context)
    : IWorkspaceTreeRepository
{
    private const string RootPath = "/";

    public async Task<WorkspaceTreeResult> GetTree(
        string path,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalizedPath = NormalizePath(path);
        var parentDirectory = await ResolveParentDirectory(normalizedPath, ct);
        if (parentDirectory is null)
        {
            return new WorkspaceTreeResult(normalizedPath, []);
        }

        var directoryChildren = await context.Directories
            .AsNoTracking()
            .Where(directory => directory.ParentId == parentDirectory.Id)
            .OrderBy(directory => directory.Name)
            .ThenBy(directory => directory.Path)
            .Select(
                directory => new WorkspaceTreeNode(
                    directory.Id,
                    directory.ParentId,
                    ToWorkspacePath(directory.Path),
                    directory.Name,
                    "folder",
                    context.Directories.Any(child => child.ParentId == directory.Id) ||
                    context.Documents.Any(document => document.DirectoryId == directory.Id),
                    context.Directories.Count(child => child.ParentId == directory.Id) +
                    context.Documents.Count(document => document.DirectoryId == directory.Id),
                    true))
            .ToArrayAsync(ct);
        var documentChildren = await (
                from document in context.Documents.AsNoTracking()
                where document.DirectoryId == parentDirectory.Id
                let projectName = context.ProjectNodes.Count(project => project.ProjectDocumentId == document.Id) == 1
                    ? context.ProjectNodes
                        .Where(project => project.ProjectDocumentId == document.Id)
                        .Select(project => project.Name)
                        .FirstOrDefault()
                    : null
                orderby document.Kind == DocumentKind.ProjectFile ? 0 : 1, projectName ?? document.FileName, document.RelativePath
                select new WorkspaceTreeNode(
                    document.Id,
                    parentDirectory.Id,
                    document.RelativePath,
                    projectName ?? document.FileName,
                    document.Kind == DocumentKind.ProjectFile ? "project" : "file",
                    false,
                    null,
                    false))
            .ToArrayAsync(ct);
        var nodes = directoryChildren
            .Concat(documentChildren)
            .OrderBy(
                node => node.Kind == "folder"
                    ? 0
                    : node.Kind == "project"
                    ? 1
                    : 2)
            .ThenBy(node => node.Label)
            .ThenBy(node => node.Path)
            .ToArray();

        return new WorkspaceTreeResult(normalizedPath, nodes, parentDirectory.Id);
    }

    private async Task<DirectoryRecord?> ResolveParentDirectory(
        string normalizedPath,
        CancellationToken ct)
    {
        var persistedPath = string.Equals(normalizedPath, RootPath, StringComparison.Ordinal)
            ? string.Empty
            : normalizedPath;

        return await context.Directories
            .AsNoTracking()
            .FirstOrDefaultAsync(directory => directory.Path == persistedPath, ct);
    }

    private static string NormalizePath(string path)
    {
        var trimmedPath = path.Trim()
            .Replace('\\', '/');
        if (string.Equals(trimmedPath, RootPath, StringComparison.Ordinal))
        {
            return RootPath;
        }

        return trimmedPath.Trim('/');
    }

    private static string ToWorkspacePath(string path)
        => string.IsNullOrEmpty(path)
            ? RootPath
            : path;
}
