using AwesomeAssertions;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class WorkspaceFileDiscovererTests
{
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
