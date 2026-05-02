using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Refactoring;

internal sealed class WorkspaceTargetResolver(
    IRepositoryWorkspace repositoryWorkspace,
    IFileSystem fileSystem)
{
    public TargetPathResolution Resolve(string? targetPath)
    {
        if (!string.IsNullOrWhiteSpace(targetPath))
        {
            return ResolveExplicitTarget(targetPath);
        }

        var solutionCandidates = GetCandidates("*.sln");
        if (solutionCandidates.Length == 1)
        {
            return Success(solutionCandidates[0]);
        }

        if (solutionCandidates.Length > 1)
        {
            return Ambiguous("solution", solutionCandidates);
        }

        var projectCandidates = GetCandidates("*.csproj");
        if (projectCandidates.Length == 1)
        {
            return Success(projectCandidates[0]);
        }

        if (projectCandidates.Length > 1)
        {
            return Ambiguous("project", projectCandidates);
        }

        return new TargetPathResolution(
            false,
            string.Empty,
            $"No .sln or .csproj target was found under '{repositoryWorkspace.RootPath}'. Specify --target.");
    }

    private TargetPathResolution ResolveExplicitTarget(string targetPath)
    {
        var absoluteTargetPath = fileSystem.Path.GetFullPath(
            fileSystem.Path.IsPathRooted(targetPath)
                ? targetPath
                : fileSystem.Path.Combine(repositoryWorkspace.RootPath, targetPath));
        if (!repositoryWorkspace.IsSameOrSubPath(absoluteTargetPath))
        {
            return new TargetPathResolution(
                false,
                string.Empty,
                $"Target path '{absoluteTargetPath}' must be located under repository root '{repositoryWorkspace.RootPath}'.");
        }

        if (!fileSystem.File.Exists(absoluteTargetPath))
        {
            return new TargetPathResolution(
                false,
                string.Empty,
                $"Target file '{absoluteTargetPath}' was not found.");
        }

        if (!IsSupportedTargetPath(absoluteTargetPath))
        {
            return new TargetPathResolution(
                false,
                string.Empty,
                $"Target path '{FormatPath(absoluteTargetPath)}' must point to a .sln or .csproj file.");
        }

        return Success(absoluteTargetPath);
    }

    private string[] GetCandidates(string searchPattern)
        => fileSystem.Directory
            .GetFiles(
                repositoryWorkspace.RootPath,
                searchPattern,
                SearchOption.TopDirectoryOnly)
            .Select(fileSystem.Path.GetFullPath)
            .OrderBy(static path => path, GetPathComparer())
            .ToArray();

    private TargetPathResolution Ambiguous(
        string targetKind,
        IReadOnlyCollection<string> candidatePaths)
    {
        var formattedCandidates = string.Join(
            ", ",
            candidatePaths.Select(FormatPath));

        return new TargetPathResolution(
            false,
            string.Empty,
            $"Multiple {targetKind} targets were found under '{repositoryWorkspace.RootPath}': {formattedCandidates}. Specify --target.");
    }

    private TargetPathResolution Success(string absoluteTargetPath)
        => new(
            true,
            absoluteTargetPath,
            string.Empty);

    private string FormatPath(string absolutePath)
        => repositoryWorkspace.TryToRepositoryRelativePath(absolutePath, out var relativePath)
            ? relativePath
            : absolutePath;

    private static bool IsSupportedTargetPath(string targetPath)
    {
        var extension = Path.GetExtension(targetPath);

        return string.Equals(extension, ".sln", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}

internal readonly record struct TargetPathResolution(
    bool Success,
    string TargetPath,
    string ErrorMessage);
