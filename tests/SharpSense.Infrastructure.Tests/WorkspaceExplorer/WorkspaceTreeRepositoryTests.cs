using AwesomeAssertions;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.WorkspaceExplorer;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.WorkspaceExplorer;

public sealed class WorkspaceTreeRepositoryTests
{
    [Fact]
    public async Task WhenReadingRoot_ThenReturnsOrderedTopLevelRows()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct: TestContext.Current.CancellationToken);
        await SeedTree(context);
        var repository = new WorkspaceTreeRepository(context);

        var result = await repository.GetTree("/", TestContext.Current.CancellationToken);

        result.ParentPath.Should().Be("/");
        result.ParentDirectoryId.Should().Be(1);
        result.Nodes
            .Select(static node => node.Path)
            .Should()
            .Equal("docs", "root", "SharpSense.App.csproj", "README.md");
    }

    [Fact]
    public async Task WhenReadingChildPath_ThenReturnsImmediateChildrenOnly()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct: TestContext.Current.CancellationToken);
        await SeedTree(context);
        var repository = new WorkspaceTreeRepository(context);

        var result = await repository.GetTree("docs", TestContext.Current.CancellationToken);

        result.ParentPath.Should().Be("docs");
        result.ParentDirectoryId.Should().Be(2);
        result.Nodes.Should().ContainSingle();
        result.Nodes[0].Path.Should().Be("docs/Guide.md");
        result.Nodes[0].Kind.Should().Be("file");
    }

    [Fact]
    public async Task WhenReadingFolderNamedRoot_ThenReturnsItsChildren()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct: TestContext.Current.CancellationToken);
        await SeedTree(context);
        var repository = new WorkspaceTreeRepository(context);

        var result = await repository.GetTree("root", TestContext.Current.CancellationToken);

        result.ParentPath.Should().Be("root");
        result.Nodes
            .Select(static node => node.Path)
            .Should()
            .Equal("root/child.txt");
    }

    private static async Task SeedTree(SharpSenseDbContext context)
    {
        context.Directories.AddRange(
            new DirectoryRecord
            {
                Id = 1,
                Path = string.Empty,
                Name = "/"
            },
            new DirectoryRecord
            {
                Id = 2,
                ParentId = 1,
                Path = "docs",
                Name = "docs"
            },
            new DirectoryRecord
            {
                Id = 3,
                ParentId = 1,
                Path = "root",
                Name = "root"
            });
        context.DirectoryClosures.AddRange(
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = 1,
                DescendantDirectoryId = 1,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = 2,
                DescendantDirectoryId = 2,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = 3,
                DescendantDirectoryId = 3,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = 1,
                DescendantDirectoryId = 2,
                Depth = 1
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = 1,
                DescendantDirectoryId = 3,
                Depth = 1
            });
        context.Documents.AddRange(
            new DocumentRecord
            {
                Id = 10,
                DirectoryId = 1,
                FileName = "SharpSense.App.csproj",
                Extension = ".csproj",
                RelativePath = "SharpSense.App.csproj",
                Kind = DocumentKind.ProjectFile
            },
            new DocumentRecord
            {
                Id = 11,
                DirectoryId = 1,
                FileName = "README.md",
                Extension = ".md",
                RelativePath = "README.md",
                Kind = DocumentKind.Markdown
            },
            new DocumentRecord
            {
                Id = 12,
                DirectoryId = 2,
                FileName = "Guide.md",
                Extension = ".md",
                RelativePath = "docs/Guide.md",
                Kind = DocumentKind.Markdown
            },
            new DocumentRecord
            {
                Id = 13,
                DirectoryId = 3,
                FileName = "child.txt",
                Extension = ".txt",
                RelativePath = "root/child.txt",
                Kind = DocumentKind.Other
            });
        context.GraphNodes.Add(
            new GraphNodeRecord
            {
                Id = 100,
                CanonicalId = "project:SharpSense.App.csproj",
                Kind = GraphNodeKind.Project
            });
        context.ProjectNodes.Add(
            new ProjectNodeRecord
            {
                Id = 100,
                Name = "SharpSense.App",
                ProjectDocumentId = 10,
                ContentHash = "project-root"
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
