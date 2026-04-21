using SharpSense.Infrastructure.Storage;
using System.Security.Cryptography;
using System.Text;

namespace SharpSense.Infrastructure.Tests.Storage;

public sealed class RepositoryWorkspaceTests
{
    [Fact]
    public void WhenConvertingAbsolutePathToRepositoryRelativePath_ThenReturnsNormalizedPath()
    {
        var repositoryRoot = CreateRepositoryRoot();

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
            var filePath = Path.Combine(repositoryRoot, "src", "SharpSense.Domain", "KnowledgeGraph", "Nodes", "ProjectNode.cs");

            var relativePath = workspace.ToRepositoryRelativePath(filePath);

            Assert.Equal("src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs", relativePath);
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public void WhenConvertingRelativePathToRepositoryRelativePath_ThenReturnsNormalizedPath()
    {
        var repositoryRoot = CreateRepositoryRoot();

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
            var filePath = Path.Combine("src", "SharpSense.Domain", ".", "KnowledgeGraph", "Nodes", "ProjectNode.cs");

            var relativePath = workspace.ToRepositoryRelativePath(filePath);

            Assert.Equal("src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs", relativePath);
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public void WhenConvertingPathOutsideRepositoryRoot_ThenThrowsInvalidOperationException()
    {
        var repositoryRoot = CreateRepositoryRoot();

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
            var filePath = Path.Combine(Path.GetDirectoryName(repositoryRoot)!, "outside", "ProjectNode.cs");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                workspace.ToRepositoryRelativePath(filePath));

            Assert.Contains("must be located under repository root", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public void WhenCreatingWorkspaceFromDirectoryWithGitAncestor_ThenUsesGitAncestorAsRootPath()
    {
        var testRoot = Path.Combine(AppContext.BaseDirectory, $"repository-path-{Guid.NewGuid():N}");
        var nestedDirectory = Path.Combine(testRoot, "src", "Sample");
        var solutionPath = Path.Combine(nestedDirectory, "Sample.sln");

        Directory.CreateDirectory(Path.Combine(testRoot, ".git"));
        Directory.CreateDirectory(nestedDirectory);
        File.WriteAllText(solutionPath, string.Empty);

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(Path.GetDirectoryName(solutionPath)!);

            Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(testRoot)), workspace.RootPath);
        }
        finally
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void WhenCreatingWorkspaceFromWorkingDirectory_ThenUsesRepositoryHashDatabasePath()
    {
        var repositoryRootPath = CreateRepositoryRoot();

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRootPath);
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRootPath));
            var hashInput = normalizedRoot.Replace('\\', '/');

            if (OperatingSystem.IsWindows() && hashInput is [_, ':', ..])
            {
                hashInput = char.ToUpperInvariant(hashInput[0]) + hashInput[1..];
            }

            var expectedHash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(hashInput))).ToLowerInvariant();
            var expectedPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".SharpSense",
                $"{expectedHash}.db");

            Assert.Equal(expectedPath, workspace.DatabasePath);
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRootPath);
        }
    }

    [Fact]
    public void WhenCheckingPathOutsideRepositoryRoot_ThenReturnsFalse()
    {
        var repositoryRoot = CreateRepositoryRoot();

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
            var filePath = Path.Combine(Path.GetDirectoryName(repositoryRoot)!, "outside", "ProjectNode.cs");

            Assert.False(workspace.IsSameOrSubPath(filePath));
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public void WhenGettingRequiredSolutionDirectoryPath_ThenReturnsSolutionDirectory()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var solutionDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var solutionPath = Path.Combine(solutionDirectory, "Sample.sln");

        Directory.CreateDirectory(solutionDirectory);
        File.WriteAllText(solutionPath, string.Empty);

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);

            var resolvedDirectory = workspace.GetRequiredSolutionDirectoryPath(solutionPath);

            Assert.Equal(Path.GetFullPath(solutionDirectory), resolvedDirectory);
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public void WhenLoadingSharpSenseConfig_ThenReadsYamlFromSolutionDirectory()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var solutionDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var solutionPath = Path.Combine(solutionDirectory, "Sample.sln");
        var configPath = Path.Combine(solutionDirectory, "sharpsense.yaml");

        Directory.CreateDirectory(solutionDirectory);
        File.WriteAllText(solutionPath, string.Empty);
        File.WriteAllText(
            configPath,
            """
            includePaths:
              - docs/**/*.md
              - README.md
            """);

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);

            var config = workspace.LoadSharpSenseConfig(solutionPath);

            Assert.Equal(["docs/**/*.md", "README.md"], config.IncludePaths);
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    private static string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"sharp-sense-workspace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".git"));
        return repositoryRoot;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
