using AwesomeAssertions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Context360;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Tests.TestData;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Context360;

public sealed class ContextRepositoryTests
{
    [Fact]
    public async Task WhenGetNodeContextHasRelatedMethods_ThenItSanitizesRelatedNodeNamesInProjection()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct);
        await KnowledgeGraphFixture.SeedAsync(context);
        var repository = new ContextRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await repository.GetNodeContext(
            KnowledgeGraphFixture.TargetNodeId,
            10,
            ct);

        result.Should().NotBeNull();
        result!.TargetNode.Name.Should().Be(KnowledgeGraphFixture.TargetDisplayName);
        result.Callers.Select(static node => node.Name).Should().Equal(
            "MessageConsumer.Render",
            "ServiceRegistration.Configure");
        result.Implementers.Should().BeEmpty();
        result.Callees.Select(static node => node.Name).Should().Equal(
            "MessageFormatter.Format",
            "Message");
        result.Inherits.Should().BeEmpty();
        result.Parents.Select(static node => node.Name).Should().Equal("MessageProvider");
        result.Children.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTargetNodeIsMissing_ThenItReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct);
        await KnowledgeGraphFixture.SeedAsync(context);
        var repository = new ContextRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await repository.GetNodeContext(
            999,
            10,
            ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task WhenGetNodeContextTargetsType_ThenItSeparatesStructuralHierarchy()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct);
        await KnowledgeGraphFixture.SeedAsync(context);
        var repository = new ContextRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await repository.GetNodeContext(
            KnowledgeGraphFixture.MessageProviderTypeNodeId,
            10,
            ct);

        result.Should().NotBeNull();
        result!.Callers.Should().BeEmpty();
        result.Implementers.Should().BeEmpty();
        result.Callees.Should().BeEmpty();
        result.Inherits.Should().BeEmpty();
        result.Parents.Select(static node => node.Name).Should().Equal("Fixture.App");
        result.Children.Select(static node => node.Name).Should().Equal(
            "MessageProvider.GetCachedMessage",
            "MessageProvider.GetMessage");
    }

    [Fact]
    public async Task WhenGetNodeContextTargetsMarkdownHeading_ThenItShowsStructuralParentWithoutFunctionalDuplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct);
        await SeedMarkdownHierarchyAsync(context, ct);
        var repository = new ContextRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await repository.GetNodeContext(
            101,
            10,
            ct);

        result.Should().NotBeNull();
        result!.Callers.Should().BeEmpty();
        result.Implementers.Should().BeEmpty();
        result.Callees.Should().BeEmpty();
        result.Inherits.Should().BeEmpty();
        result.Parents.Select(static node => node.Name).Should().Equal("Guide");
        result.Children.Should().BeEmpty();
    }

    private static async Task SeedMarkdownHierarchyAsync(
        SharpSenseDbContext context,
        CancellationToken ct)
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
                AncestorDirectoryId = 1,
                DescendantDirectoryId = 2,
                Depth = 1
            });
        context.Documents.Add(
            new DocumentRecord
            {
                Id = 10,
                DirectoryId = 2,
                FileName = "Guide.md",
                Extension = ".md",
                RelativePath = "docs/Guide.md",
                Kind = DocumentKind.Source
            });
        context.GraphNodes.AddRange(
            new GraphNodeRecord
            {
                Id = 100,
                CanonicalId = "code:doc:docs/Guide.md#document-root",
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = 101,
                CanonicalId = "code:doc:docs/Guide.md#overview",
                Kind = GraphNodeKind.Code
            });
        context.CodeNodes.AddRange(
            new CodeNodeRecord
            {
                Id = 100,
                DocumentId = 10,
                FullyQualifiedName = "docs/Guide.md#document-root",
                DisplayName = "Guide",
                NodeType = NodeType.Document,
                StartLine = 1,
                EndLine = 1,
                SearchText = "Guide",
                Summary = string.Empty
            },
            new CodeNodeRecord
            {
                Id = 101,
                DocumentId = 10,
                FullyQualifiedName = "docs/Guide.md#overview",
                DisplayName = "Guide#overview",
                NodeType = NodeType.Document,
                StartLine = 1,
                EndLine = 2,
                SearchText = "Guide#overview",
                Summary = string.Empty
            });
        context.DependencyEdges.AddRange(
            new DependencyEdgeRecord
            {
                CallerNodeId = 100,
                CalleeNodeId = 101,
                EdgeType = EdgeType.ParentOf
            });

        await context.SaveChangesAsync(ct);
    }
}
