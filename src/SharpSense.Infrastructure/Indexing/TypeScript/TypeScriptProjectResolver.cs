using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptProjectResolver(
    IRepositoryWorkspace repositoryWorkspace,
    IFileSystem fileSystem)
{
    private static readonly JsonDocumentOptions _jsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public TypeScriptProjectResolution Resolve(
        IReadOnlyList<TypeScriptParsedFile> parsedFiles,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parsedFiles);

        var projectsByManifestPath = new Dictionary<string, IndexedProject>(FileSystemPaths.Comparer);
        var projectIdByFilePath = new Dictionary<string, string>(StringComparer.Ordinal);
        var manifestPaths = new HashSet<string>(FileSystemPaths.Comparer);

        foreach (var parsedFile in parsedFiles)
        {
            ct.ThrowIfCancellationRequested();

            var manifestPath = FindNearestManifestPath(parsedFile.DiscoveredFile.AbsolutePath);
            if (manifestPath is null)
            {
                continue;
            }

            if (!projectsByManifestPath.TryGetValue(manifestPath, out var project))
            {
                project = CreateIndexedProject(manifestPath);
                projectsByManifestPath[manifestPath] = project;
                manifestPaths.Add(manifestPath);
            }

            projectIdByFilePath[parsedFile.DiscoveredFile.RelativeFilePath] = project.Id;
        }

        var projects = projectsByManifestPath.Values
            .OrderBy(static project => project.Name, StringComparer.Ordinal)
            .ThenBy(static project => project.Id, StringComparer.Ordinal)
            .ToArray();

        return new TypeScriptProjectResolution(projects, projectIdByFilePath, [.. manifestPaths]);
    }

    private string? FindNearestManifestPath(string absoluteFilePath)
    {
        var directory = fileSystem.Path.GetDirectoryName(absoluteFilePath);
        string? nearestTsConfig = null;

        while (!string.IsNullOrWhiteSpace(directory) &&
               repositoryWorkspace.IsSameOrSubPath(directory))
        {
            var packageJsonPath = fileSystem.Path.Combine(directory, "package.json");
            if (fileSystem.File.Exists(packageJsonPath))
            {
                return packageJsonPath;
            }

            if (nearestTsConfig is null)
            {
                var tsConfigPath = fileSystem.Path.Combine(directory, "tsconfig.json");
                if (fileSystem.File.Exists(tsConfigPath))
                {
                    nearestTsConfig = tsConfigPath;
                }
            }

            if (FileSystemPaths.Comparer.Equals(directory, repositoryWorkspace.RootPath))
            {
                break;
            }

            directory = fileSystem.Path.GetDirectoryName(directory);
        }

        return nearestTsConfig;
    }

    private IndexedProject CreateIndexedProject(string manifestPath)
    {
        var relativeManifestPath = repositoryWorkspace
            .ToRepositoryRelativePath(manifestPath)
            .Replace('\\', '/');
        var contentBytes = fileSystem.File.ReadAllBytes(manifestPath);
        var contentHash = Convert.ToHexString(SHA256.HashData(contentBytes))
            .ToLowerInvariant();
        var name = ResolveProjectName(manifestPath, contentBytes);

        return new IndexedProject(
            $"project:ts:{relativeManifestPath}",
            name,
            relativeManifestPath,
            contentHash);
    }

    private string ResolveProjectName(string manifestPath, byte[] contentBytes)
    {
        if (string.Equals(
            fileSystem.Path.GetFileName(manifestPath),
            "package.json",
            StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(contentBytes, _jsonOptions);
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("name", out var nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String)
                {
                    var packageName = nameElement.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(packageName))
                    {
                        return packageName;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to directory name if package.json is not valid JSON.
            }
        }

        var directoryPath = fileSystem.Path.GetDirectoryName(manifestPath);
        var directoryName = string.IsNullOrWhiteSpace(directoryPath)
            ? null
            : fileSystem.Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!string.IsNullOrWhiteSpace(directoryName))
        {
            return directoryName;
        }

        return fileSystem.Path.GetFileName(
            repositoryWorkspace.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
