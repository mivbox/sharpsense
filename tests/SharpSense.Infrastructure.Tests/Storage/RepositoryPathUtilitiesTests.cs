using SharpSense.Infrastructure.Storage;

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
            var expectedHash = RepositoryHashCalculator.ComputeHash(workspace.RootPath);
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
    public void WhenGettingRequiredTargetDirectoryPath_ThenReturnsTargetDirectory()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var targetPath = Path.Combine(targetDirectory, "Sample.sln");

        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(targetPath, string.Empty);

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);

            var resolvedDirectory = workspace.GetRequiredTargetDirectoryPath(targetPath);

            Assert.Equal(Path.GetFullPath(targetDirectory), resolvedDirectory);
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public void WhenGettingRequiredTargetDirectoryPathForProjectFile_ThenReturnsProjectDirectory()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var targetPath = Path.Combine(targetDirectory, "Sample.csproj");

        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(targetPath, string.Empty);

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);

            var resolvedDirectory = workspace.GetRequiredTargetDirectoryPath(targetPath);

            Assert.Equal(Path.GetFullPath(targetDirectory), resolvedDirectory);
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
