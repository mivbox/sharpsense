using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class WorkspaceFileDiscovererTests
{
    [Fact]
    public async Task WhenGitIgnoreExists_ThenFiltersIgnoredMatches()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var includedPath = Path.Combine(targetDirectory, "docs", "Guide.md");
        var ignoredPath = Path.Combine(targetDirectory, "docs", "Ignored.md");

        Directory.CreateDirectory(Path.GetDirectoryName(includedPath)!);
        File.WriteAllText(Path.Combine(repositoryRoot, ".gitignore"), "src/Sample/docs/Ignored.md");
        File.WriteAllText(includedPath, "# Guide");
        File.WriteAllText(ignoredPath, "# Ignored");

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
            var discoverer = new WorkspaceFileDiscoverer(workspace);

            var files = await discoverer.GetAllowedFiles(
                targetDirectory,
                ["docs/**/*.md"],
                TestContext.Current.CancellationToken);

            Assert.Collection(
                files,
                file =>
                {
                    Assert.Equal(includedPath, file.AbsolutePath);
                    Assert.Equal("src/Sample/docs/Guide.md", file.RelativeFilePath);
                });
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    [Fact]
    public async Task WhenGitIgnoreIsMissing_ThenReturnsGlobMatches()
    {
        var repositoryRoot = CreateRepositoryRoot();
        var targetDirectory = Path.Combine(repositoryRoot, "src", "Sample");
        var firstPath = Path.Combine(targetDirectory, "docs", "Guide.md");
        var secondPath = Path.Combine(targetDirectory, "docs", "Reference.md");

        Directory.CreateDirectory(Path.GetDirectoryName(firstPath)!);
        File.WriteAllText(firstPath, "# Guide");
        File.WriteAllText(secondPath, "# Reference");

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
            var discoverer = new WorkspaceFileDiscoverer(workspace);

            var files = await discoverer.GetAllowedFiles(
                targetDirectory,
                ["docs/**/*.md"],
                TestContext.Current.CancellationToken);

            Assert.Equal(2, files.Count);
            Assert.Collection(
                files,
                file => Assert.Equal("src/Sample/docs/Guide.md", file.RelativeFilePath),
                file => Assert.Equal("src/Sample/docs/Reference.md", file.RelativeFilePath));
        }
        finally
        {
            DeleteDirectoryIfExists(repositoryRoot);
        }
    }

    private static string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"sharp-sense-file-discovery-{Guid.NewGuid():N}");
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
