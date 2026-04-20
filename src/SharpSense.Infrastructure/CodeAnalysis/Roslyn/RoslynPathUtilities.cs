using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal static class RoslynPathUtilities
{
    public static string GetRequiredProjectFilePath(Project project)
    {
        return string.IsNullOrWhiteSpace(project.FilePath)
            ? throw new InvalidOperationException($"Project '{project.Name}' does not have a file path.")
            : project.FilePath;
    }

    public static string? GetRelativeSyntaxTreePath(
        Project project,
        SyntaxTree syntaxTree,
        IRepositoryWorkspace repositoryWorkspace,
        ConcurrentQueue<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (string.IsNullOrWhiteSpace(syntaxTree.FilePath))
        {
            diagnostics.Enqueue($"Skipping document without a file path in project '{project.Name}'.");
            return null;
        }

        if (Path.IsPathRooted(syntaxTree.FilePath))
        {
            if (repositoryWorkspace.TryToRepositoryRelativePath(syntaxTree.FilePath, out var relativeFilePath))
            {
                return relativeFilePath;
            }

            diagnostics.Enqueue($"Skipping document outside repository root: '{syntaxTree.FilePath}'.");
            return null;
        }

        var projectRelativePath = repositoryWorkspace.ToRepositoryRelativePath(GetRequiredProjectFilePath(project));
        var projectDirectory = Path.GetDirectoryName(projectRelativePath) ?? string.Empty;

        return Path.Combine(projectDirectory, "obj", "generated", syntaxTree.FilePath)
            .Replace('\\', '/');
    }

    public static string ComputeContentHash(string filePath)
    {
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filePath)));
    }
}
