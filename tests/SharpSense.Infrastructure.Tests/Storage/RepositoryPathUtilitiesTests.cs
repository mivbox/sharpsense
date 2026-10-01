using AwesomeAssertions;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Storage;

public sealed class RepositoryWorkspaceTests
{
    [Fact]
    public void WhenConvertingAbsolutePathToRepositoryRelativePath_ThenReturnsNormalizedPath()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateWorkspace(fileSystem, "/repo");
        var filePath = "/repo/src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs";

        var relativePath = workspace.ToRepositoryRelativePath(filePath);

        relativePath.Should().Be("src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs");
    }

    [Fact]
    public void WhenConvertingRelativePathToRepositoryRelativePath_ThenReturnsNormalizedPath()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateWorkspace(fileSystem, "/repo");

        var relativePath = workspace.ToRepositoryRelativePath("src/SharpSense.Domain/./KnowledgeGraph/Nodes/ProjectNode.cs");

        relativePath.Should().Be("src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs");
    }

    [Fact]
    public void WhenConvertingPathOutsideRepositoryRoot_ThenThrowsInvalidOperationException()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateWorkspace(fileSystem, "/repo");

        var act = () => workspace.ToRepositoryRelativePath("/outside/ProjectNode.cs");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*must be located under repository root*");
    }

    [Fact]
    public void WhenCreatingWorkspaceFromDirectoryWithGitAncestor_ThenKeepsTheConfiguredRoot()
    {
        var fileSystem = CreateRepositoryFileSystem();

        fileSystem.AddDirectory("/repo/src/Sample");
        fileSystem.AddFile("/repo/src/Sample/Sample.sln", new MockFileData(string.Empty));

        var workspace = CreateWorkspace(fileSystem, "/repo/src/Sample");

        workspace.RootPath.Should().Be("/repo/src/Sample");
    }

    [Fact]
    public void WhenCreatingRegisteredWorkspace_ThenUsesWorkspaceOwnedDatabasePath()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var selection = new WorkspaceCatalog(fileSystem, "/workspace-home").Create("test", "/repo", []);
        var expectedPath = Path.Combine(
            "/workspace-home",
            "workspaces",
            selection.Definition.Id.ToString("D"),
            "index.db");

        selection.Workspace.DatabasePath.Should().Be(expectedPath);
    }

    [Fact]
    public void WhenCheckingPathOutsideRepositoryRoot_ThenReturnsFalse()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateWorkspace(fileSystem, "/repo");

        workspace.IsSameOrSubPath("/outside/ProjectNode.cs").Should().BeFalse();
    }

    [Fact]
    public void WhenGettingRequiredTargetDirectoryPath_ThenReturnsTargetDirectory()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddDirectory("/repo/src/Sample");
        fileSystem.AddFile("/repo/src/Sample/Sample.sln", new MockFileData(string.Empty));
        var workspace = CreateWorkspace(fileSystem, "/repo");

        var resolvedDirectory = workspace.GetRequiredTargetDirectoryPath("/repo/src/Sample/Sample.sln");

        resolvedDirectory.Should().Be("/repo/src/Sample");
    }

    [Fact]
    public void WhenGettingRequiredTargetDirectoryPathForProjectFile_ThenReturnsProjectDirectory()
    {
        var fileSystem = CreateRepositoryFileSystem();
        fileSystem.AddDirectory("/repo/src/Sample");
        fileSystem.AddFile("/repo/src/Sample/Sample.csproj", new MockFileData(string.Empty));
        var workspace = CreateWorkspace(fileSystem, "/repo");

        var resolvedDirectory = workspace.GetRequiredTargetDirectoryPath("/repo/src/Sample/Sample.csproj");

        resolvedDirectory.Should().Be("/repo/src/Sample");
    }

    private static MockFileSystem CreateRepositoryFileSystem()
    {
        return new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs"] = new("namespace SharpSense.Domain;")
            },
            "/repo");
    }

    private static IRepositoryWorkspace CreateWorkspace(
        MockFileSystem fileSystem,
        string workingDirectory)
        => new RepositoryWorkspace(workingDirectory, "/test-storage/index.db", fileSystem);
}
