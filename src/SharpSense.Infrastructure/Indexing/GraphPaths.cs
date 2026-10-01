using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Indexing;

internal static class GraphPaths
{
    internal static void AddDirectoryPath(
        ISet<string> directoryPaths,
        string directoryPath)
    {
        var currentPath = directoryPath;

        while (true)
        {
            directoryPaths.Add(currentPath);
            var parentPath = GetParentDirectoryPath(currentPath);
            if (parentPath is null)
            {
                return;
            }

            currentPath = parentPath;
        }
    }

    internal static string NormalizeRelativePath(string path)
        => path.Trim()
            .Replace('\\', '/')
            .Trim('/');

    internal static string NormalizeDirectoryPath(string path)
        => string.IsNullOrWhiteSpace(path) ? string.Empty : NormalizeRelativePath(path);

    internal static string GetDirectoryPath(string relativePath)
    {
        var separatorIndex = relativePath.LastIndexOf('/');

        return separatorIndex < 0 ? string.Empty : relativePath[..separatorIndex];
    }

    internal static string? GetParentDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
        {
            return null;
        }

        var separatorIndex = directoryPath.LastIndexOf('/');

        return separatorIndex < 0 ? string.Empty : directoryPath[..separatorIndex];
    }

    internal static string GetFileName(string relativePath)
    {
        var separatorIndex = relativePath.LastIndexOf('/');

        return separatorIndex < 0 ? relativePath : relativePath[(separatorIndex + 1)..];
    }

    internal static DocumentKind GetDocumentKind(string relativePath, bool isProjectDocument)
    {
        if (isProjectDocument)
        {
            return DocumentKind.ProjectFile;
        }

        if (MarkdownFileTypes.IsMarkdown(relativePath))
        {
            return DocumentKind.Markdown;
        }

        return Path.GetExtension(relativePath) switch
        {
            ".cs" or ".ts" or ".tsx" or ".js" or ".jsx" => DocumentKind.Source,
            _ => DocumentKind.Other
        };
    }
}
