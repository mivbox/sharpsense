using AwesomeAssertions;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class WorkspaceFileDiscovererTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenExternalDirectoryIsLinked_ThenOnlyLocalDocumentsAreDiscovered(bool ignored)
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("sharpsense-discovery-");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(directory.FullName, "repo"));
            var external = Directory.CreateDirectory(Path.Combine(directory.FullName, "external"));
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "Guide.md"), "# Local", ct);
            await File.WriteAllTextAsync(Path.Combine(external.FullName, "External.md"), "# External", ct);
            await File.WriteAllTextAsync(Path.Combine(root.FullName, ".gitignore"), ignored ? "vendor/\n" : "", ct);
            Directory.CreateSymbolicLink(Path.Combine(root.FullName, "vendor"), external.FullName);
            File.CreateSymbolicLink(Path.Combine(root.FullName, "Linked.md"), Path.Combine(external.FullName, "External.md"));
            var fileSystem = new FileSystem();
            var workspace = new RepositoryWorkspace(root.FullName, Path.Combine(directory.FullName, "unused.db"), fileSystem);
            var discoverer = new WorkspaceFileDiscoverer(workspace, fileSystem);

            var files = await discoverer.GetAllowedFiles(root.FullName, ["**/*.md"], ct);

            files.Should().ContainSingle().Which.RelativeFilePath.Should().Be("Guide.md");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WhenGitIgnoreExists_ThenFiltersIgnoredMatches()
    {
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/.gitignore"] = new("src/Sample/docs/Ignored.md"),
                ["/repo/src/Sample/docs/Guide.md"] = new("# Guide"),
                ["/repo/src/Sample/docs/Ignored.md"] = new("# Ignored")
            },
            "/repo");
        var workspace = new RepositoryWorkspace("/repo", "/test-storage/index.db", fileSystem);
        var discoverer = new WorkspaceFileDiscoverer(workspace, fileSystem);

        var files = await discoverer.GetAllowedFiles(
            "/repo/src/Sample",
            ["docs/**/*.md"],
            TestContext.Current.CancellationToken);

        files.Should().ContainSingle();
        files[0].AbsolutePath.Should().Be("/repo/src/Sample/docs/Guide.md");
        files[0].RelativeFilePath.Should().Be("src/Sample/docs/Guide.md");
    }

    [Fact]
    public async Task WhenGitIgnoreIsMissing_ThenReturnsGlobMatches()
    {
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/src/Sample/docs/Guide.md"] = new("# Guide"),
                ["/repo/src/Sample/docs/Reference.md"] = new("# Reference")
            },
            "/repo");
        var workspace = new RepositoryWorkspace("/repo", "/test-storage/index.db", fileSystem);
        var discoverer = new WorkspaceFileDiscoverer(workspace, fileSystem);

        var files = await discoverer.GetAllowedFiles(
            "/repo/src/Sample",
            ["docs/**/*.md"],
            TestContext.Current.CancellationToken);

        files.Should().HaveCount(2);
        files
            .Select(static file => file.RelativeFilePath)
            .Should()
            .Equal("src/Sample/docs/Guide.md", "src/Sample/docs/Reference.md");
    }
}
