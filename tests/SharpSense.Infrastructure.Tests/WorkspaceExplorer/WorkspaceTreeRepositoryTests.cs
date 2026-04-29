using AwesomeAssertions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.WorkspaceExplorer;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.WorkspaceExplorer;

public sealed class WorkspaceTreeRepositoryTests
{
    [Fact]
    public async Task WhenReadingRoot_ThenReturnsOrderedTopLevelRows()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);
        await SeedTree(context);
        var repository = new WorkspaceTreeRepository(context);

        var result = await repository.GetTree("/", CancellationToken.None);

        result.ParentPath.Should().Be("/");
        result.Nodes.Select(static node => node.Path)
            .Should()
            .Equal("docs", "root", "SharpSense.App.csproj", "README.md");
    }

    [Fact]
    public async Task WhenReadingChildPath_ThenReturnsImmediateChildrenOnly()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);
        await SeedTree(context);
        var repository = new WorkspaceTreeRepository(context);

        var result = await repository.GetTree("docs", CancellationToken.None);

        result.ParentPath.Should().Be("docs");
        result.Nodes.Should().ContainSingle();
        result.Nodes[0].Path.Should().Be("docs/Guide.md");
        result.Nodes[0].Kind.Should().Be("file");
    }

    [Fact]
    public async Task WhenReadingFolderNamedRoot_ThenReturnsItsChildren()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);
        await SeedTree(context);
        var repository = new WorkspaceTreeRepository(context);

        var result = await repository.GetTree("root", CancellationToken.None);

        result.ParentPath.Should().Be("root");
        result.Nodes.Select(static node => node.Path)
            .Should()
            .Equal("root/child.txt");
    }

    private static async Task SeedTree(SharpSenseDbContext context)
    {
        context.WorkspaceTreeNodes.AddRange(
            new WorkspaceTreeNode
            {
                Id = "docs",
                Path = "docs",
                Label = "docs",
                Kind = WorkspaceTreeNodeKind.Folder,
                HasChildren = true,
                ChildCount = 1,
                IsSelectable = true
            },
            new WorkspaceTreeNode
            {
                Id = "SharpSense.App.csproj",
                Path = "SharpSense.App.csproj",
                Label = "SharpSense.App",
                Kind = WorkspaceTreeNodeKind.Project,
                IsSelectable = true
            },
            new WorkspaceTreeNode
            {
                Id = "root",
                Path = "root",
                Label = "root",
                Kind = WorkspaceTreeNodeKind.Folder,
                HasChildren = true,
                ChildCount = 1,
                IsSelectable = true
            },
            new WorkspaceTreeNode
            {
                Id = "README.md",
                Path = "README.md",
                Label = "README.md",
                Kind = WorkspaceTreeNodeKind.File,
                IsSelectable = true
            },
            new WorkspaceTreeNode
            {
                Id = "docs/Guide.md",
                ParentId = "docs",
                Path = "docs/Guide.md",
                Label = "Guide.md",
                Kind = WorkspaceTreeNodeKind.File,
                IsSelectable = true
            },
            new WorkspaceTreeNode
            {
                Id = "root/child.txt",
                ParentId = "root",
                Path = "root/child.txt",
                Label = "child.txt",
                Kind = WorkspaceTreeNodeKind.File,
                IsSelectable = true
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
