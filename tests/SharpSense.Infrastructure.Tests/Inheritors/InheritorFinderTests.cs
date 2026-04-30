using AwesomeAssertions;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Inheritors;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Inheritors;

public sealed class InheritorFinderTests
{
    private const int RootDirectoryId = 1;
    private const int SourceDirectoryId = 2;
    private const int BaseDocumentId = 10;
    private const int DerivedAlphaDocumentId = 11;
    private const int DerivedBetaDocumentId = 12;
    private const int InterfaceDocumentId = 13;
    private const int InterfaceImplementerDocumentId = 14;
    private const int BaseNodeId = 100;
    private const int DerivedAlphaNodeId = 101;
    private const int DerivedBetaNodeId = 102;
    private const int InterfaceNodeId = 103;
    private const int InterfaceImplementerNodeId = 104;

    [Fact]
    public async Task WhenGetInheritorsUsesBaseClassNodeId_ThenReturnsDistinctOrderedDerivedClasses()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await SeedAsync(context);
        var finder = new InheritorFinder(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await finder.GetInheritors(
            new GetInheritorsQuery(BaseNodeId),
            TestContext.Current.CancellationToken);

        result.Should().HaveCount(2);
        result.Select(node => node.Id).Should().ContainInOrder(DerivedAlphaNodeId, DerivedBetaNodeId);
        result.Should().OnlyContain(node => node.NodeType == NodeType.Class);
        result.Select(node => node.RelativeFilePath).Should().ContainInOrder(
            "src/Fixture.App/DerivedAlpha.cs",
            "src/Fixture.App/DerivedBeta.cs");
    }

    [Fact]
    public async Task WhenGetInheritorsUsesInterfaceNodeId_ThenReturnsImplementingClasses()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(TestContext.Current.CancellationToken);
        await SeedAsync(context);
        var finder = new InheritorFinder(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await finder.GetInheritors(
            new GetInheritorsQuery(InterfaceNodeId),
            TestContext.Current.CancellationToken);

        result.Should().HaveCount(1);
        result[0].Id.Should().Be(InterfaceImplementerNodeId);
        result[0].DisplayName.Should().Be("InterfaceWidget");
    }

    private static async Task SeedAsync(SharpSenseDbContext context)
    {
        context.Directories.AddRange(
            new DirectoryRecord
            {
                Id = RootDirectoryId,
                Path = string.Empty,
                Name = "/"
            },
            new DirectoryRecord
            {
                Id = SourceDirectoryId,
                ParentId = RootDirectoryId,
                Path = "src",
                Name = "src"
            });
        context.Documents.AddRange(
            new DocumentRecord
            {
                Id = BaseDocumentId,
                DirectoryId = SourceDirectoryId,
                FileName = "BaseWidget.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/BaseWidget.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = DerivedAlphaDocumentId,
                DirectoryId = SourceDirectoryId,
                FileName = "DerivedAlpha.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/DerivedAlpha.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = DerivedBetaDocumentId,
                DirectoryId = SourceDirectoryId,
                FileName = "DerivedBeta.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/DerivedBeta.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = InterfaceDocumentId,
                DirectoryId = SourceDirectoryId,
                FileName = "IWidget.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/IWidget.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = InterfaceImplementerDocumentId,
                DirectoryId = SourceDirectoryId,
                FileName = "InterfaceWidget.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/InterfaceWidget.cs",
                Kind = DocumentKind.Source
            });
        context.GraphNodes.AddRange(
            CreateGraphNode(BaseNodeId, "node-base-widget"),
            CreateGraphNode(DerivedAlphaNodeId, "node-derived-alpha"),
            CreateGraphNode(DerivedBetaNodeId, "node-derived-beta"),
            CreateGraphNode(InterfaceNodeId, "node-interface-widget"),
            CreateGraphNode(InterfaceImplementerNodeId, "node-interface-implementer"));
        context.CodeNodes.AddRange(
            new CodeNodeRecord
            {
                Id = BaseNodeId,
                DocumentId = BaseDocumentId,
                FullyQualifiedName = "Fixture.App.BaseWidget",
                DisplayName = "BaseWidget",
                NodeType = NodeType.Class,
                StartLine = 3,
                EndLine = 18,
                Summary = "Base widget."
            },
            new CodeNodeRecord
            {
                Id = DerivedAlphaNodeId,
                DocumentId = DerivedAlphaDocumentId,
                FullyQualifiedName = "Fixture.App.DerivedAlpha",
                DisplayName = "DerivedAlpha",
                NodeType = NodeType.Class,
                StartLine = 3,
                EndLine = 16,
                Summary = "Derived alpha."
            },
            new CodeNodeRecord
            {
                Id = DerivedBetaNodeId,
                DocumentId = DerivedBetaDocumentId,
                FullyQualifiedName = "Fixture.App.DerivedBeta",
                DisplayName = "DerivedBeta",
                NodeType = NodeType.Class,
                StartLine = 3,
                EndLine = 17,
                Summary = "Derived beta."
            },
            new CodeNodeRecord
            {
                Id = InterfaceNodeId,
                DocumentId = InterfaceDocumentId,
                FullyQualifiedName = "Fixture.App.IWidget",
                DisplayName = "IWidget",
                NodeType = NodeType.Interface,
                StartLine = 3,
                EndLine = 6,
                Summary = "Widget interface."
            },
            new CodeNodeRecord
            {
                Id = InterfaceImplementerNodeId,
                DocumentId = InterfaceImplementerDocumentId,
                FullyQualifiedName = "Fixture.App.InterfaceWidget",
                DisplayName = "InterfaceWidget",
                NodeType = NodeType.Class,
                StartLine = 3,
                EndLine = 14,
                Summary = "Interface widget."
            });
        context.DependencyEdges.AddRange(
            new DependencyEdgeRecord
            {
                CallerNodeId = DerivedAlphaNodeId,
                CalleeNodeId = BaseNodeId,
                EdgeType = EdgeType.Implements
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = DerivedBetaNodeId,
                CalleeNodeId = BaseNodeId,
                EdgeType = EdgeType.Implements
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = InterfaceImplementerNodeId,
                CalleeNodeId = InterfaceNodeId,
                EdgeType = EdgeType.Implements
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static GraphNodeRecord CreateGraphNode(int id, string canonicalId)
        => new()
        {
            Id = id,
            CanonicalId = canonicalId,
            Kind = GraphNodeKind.Code
        };
}
