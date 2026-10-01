using SharpSense.Application.Indexing;
using System.IO.Abstractions;

namespace SharpSense.Cli.Workspaces;

/// <summary>
/// Offers bounded source candidates without interpreting project files or executing build tooling.
/// </summary>
internal sealed class WorkspaceSourceDiscovery(IFileSystem fileSystem)
{
    private static readonly HashSet<string> _excludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".sharpsense", "node_modules", "bin", "obj", "dist", "build", ".next", ".turbo", "coverage", "vendor"
    };

    public DiscoveredWorkspaceSources Discover(string workspaceRoot, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (!fileSystem.Path.IsPathRooted(workspaceRoot) || !fileSystem.Directory.Exists(workspaceRoot))
        {
            throw new ArgumentException(
                "Workspace root must be an existing absolute directory.",
                nameof(workspaceRoot));
        }

        var root = fileSystem.Path.GetFullPath(workspaceRoot);
        var pending = new Queue<string>();
        pending.Enqueue(root);
        var candidates = new List<WorkspaceSource>();
        var markdownDirectories = new HashSet<string>(StringComparer.Ordinal);
        var visited = 0;
        var visitedFiles = 0;
        while (pending.TryDequeue(out var directory))
        {
            ct.ThrowIfCancellationRequested();
            if (++visited > 10_000)
            {
                throw new ArgumentException("Repository contains too many source candidates to discover automatically. Add explicit sources instead.");
            }

            foreach (var file in fileSystem.Directory.EnumerateFiles(directory))
            {
                ct.ThrowIfCancellationRequested();
                if (++visitedFiles > 100_000 || candidates.Count + markdownDirectories.Count > 2_000)
                {
                    throw new ArgumentException("Repository contains too many files or source candidates to discover automatically. Add explicit sources instead.");
                }

                var extension = fileSystem.Path.GetExtension(file);
                var name = fileSystem.Path.GetFileName(file);
                var relative = fileSystem.Path.GetRelativePath(root, file)
                    .Replace('\\', '/');
                if (new[]
                { ".csproj", ".sln", ".slnx" }.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(new(WorkspaceSourceKind.CSharp, relative));
                }
                else if (name.StartsWith(
                    "tsconfig",
                    StringComparison.OrdinalIgnoreCase) && extension.Equals(
                        ".json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(new(WorkspaceSourceKind.TypeScript, relative));
                }
                else if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var relativeDirectory = fileSystem.Path.GetRelativePath(root, directory)
                        .Replace('\\', '/');
                    markdownDirectories.Add(relativeDirectory == "."
                        ? "*.md"
                        : $"{relativeDirectory}/*.md");
                }
            }

            foreach (var child in fileSystem.Directory.EnumerateDirectories(directory))
            {
                if (!_excludedDirectories.Contains(fileSystem.Path.GetFileName(child)) &&
                    (fileSystem.DirectoryInfo.New(child).Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Enqueue(child);
                }
            }
        }

        candidates.AddRange(markdownDirectories
            .Select(static path => new WorkspaceSource(
                WorkspaceSourceKind.Markdown,
                path)));

        return new DiscoveredWorkspaceSources(
            root,
            candidates
                .OrderBy(static source => source.Kind)
                .ThenBy(static source => source.Path)
                .ToArray());
    }
}
