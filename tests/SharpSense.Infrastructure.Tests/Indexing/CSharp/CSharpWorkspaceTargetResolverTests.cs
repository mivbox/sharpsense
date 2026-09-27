using AwesomeAssertions;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing.CSharp;

public sealed class CSharpWorkspaceTargetResolverTests
{
    [Theory]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    [InlineData("src/App.csproj")]
    [InlineData("src/App.CSPROJ")]
    public void WhenTargetIsCSharpWorkspace_ThenResolvesRepositoryRelativePath(string target)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
            ["/repo/" + target] = new("")
        });
        var workspace = new RepositoryWorkspace("/repo", "/test-storage/index.db", fileSystem);
        var resolver = new CSharpWorkspaceTargetResolver(workspace, fileSystem);

        resolver.ResolveTargetPath(target).Should().Be("/repo/" + target);
    }

    [Theory]
    [InlineData("/repo/tsconfig.json")]
    [InlineData("/repo/src")]
    [InlineData("/repo/src/App.tsx")]
    public void WhenTargetIsNotCSharpWorkspace_ThenSkipsRoslyn(string target)
    {
        var fileSystem = new MockFileSystem();
        var workspace = new RepositoryWorkspace("/repo", "/repo/test.db", fileSystem);
        new CSharpWorkspaceTargetResolver(workspace, fileSystem).ResolveTargetPath(target).Should().BeNull();
    }
}
