using AwesomeAssertions;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Refactoring;
using ApplicationDocumentKind = SharpSense.Application.Refactoring.Models.DocumentKind;
using PersistedDocumentKind = SharpSense.Infrastructure.Persistence.Records.DocumentKind;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Refactoring;

public sealed class RefactorTargetLookupTests
{
    [Fact]
    public async Task WhenTargetNodeExists_ThenItReturnsThePersistedPathAndLineSpan()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);
        context.Directories.Add(new DirectoryRecord
        {
            Id = 1,
            Path = string.Empty,
            Name = "/"
        });
        context.Documents.Add(new DocumentRecord
        {
            Id = 10,
            DirectoryId = 1,
            FileName = "Feature.cs",
            Extension = ".cs",
            RelativePath = "src/Fixture.App/Feature.cs",
            Kind = PersistedDocumentKind.Source
        });
        context.Documents.Add(new DocumentRecord
        {
            Id = 11,
            DirectoryId = 1,
            FileName = "Fixture.App.csproj",
            Extension = ".csproj",
            RelativePath = "src/Fixture.App/Fixture.App.csproj",
            Kind = PersistedDocumentKind.Other
        });
        context.GraphNodes.Add(new GraphNodeRecord
        {
            Id = 42,
            CanonicalId = "code:project-app:Fixture.App.Feature.Run()",
            Kind = GraphNodeKind.Code
        });
        context.GraphNodes.Add(new GraphNodeRecord
        {
            Id = 100,
            CanonicalId = "project:src/Fixture.App/Fixture.App.csproj"
        });
        context.ProjectNodes.Add(new ProjectNodeRecord
        {
            Id = 100,
            Name = "Fixture.App",
            ProjectDocumentId = 11,
            ContentHash = "hash"
        });
        context.CodeNodes.Add(new CodeNodeRecord
        {
            Id = 42,
            ProjectNodeId = 100,
            DocumentId = 10,
            FullyQualifiedName = "Fixture.App.Feature.Run()",
            DisplayName = "Feature.Run()",
            NodeType = NodeType.Method,
            StartLine = 12,
            EndLine = 18,
            Summary = "Runs the feature."
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var lookup = new RefactorTargetLookup(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await lookup.GetTarget(42, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(new
        {
            NodeId = 42,
            RelativeFilePath = "src/Fixture.App/Feature.cs",
            StartLine = 12,
            EndLine = 18,
            RelativeProjectPath = "src/Fixture.App/Fixture.App.csproj",
            DocumentKind = ApplicationDocumentKind.Source
        });
    }

    [Fact]
    public async Task WhenTargetNodePointsAtMarkdownDocument_ThenItReturnsMarkdownKindWithoutProjectPath()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);
        context.Directories.Add(new DirectoryRecord
        {
            Id = 1,
            Path = string.Empty,
            Name = "/"
        });
        context.Documents.Add(new DocumentRecord
        {
            Id = 10,
            DirectoryId = 1,
            FileName = "Guide.md",
            Extension = ".md",
            RelativePath = "docs/Guide.md",
            Kind = PersistedDocumentKind.Markdown
        });
        context.GraphNodes.Add(new GraphNodeRecord
        {
            Id = 42,
            CanonicalId = "code:doc:docs/Guide.md#getting-started",
            Kind = GraphNodeKind.Code
        });
        context.CodeNodes.Add(new CodeNodeRecord
        {
            Id = 42,
            DocumentId = 10,
            FullyQualifiedName = "Guide#getting-started",
            DisplayName = "Guide#getting-started",
            NodeType = NodeType.Document,
            StartLine = 1,
            EndLine = 3,
            Summary = "Getting started."
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var lookup = new RefactorTargetLookup(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await lookup.GetTarget(42, TestContext.Current.CancellationToken);

        result.Should().NotBeNull();
        result.Should().BeEquivalentTo(new
        {
            NodeId = 42,
            RelativeFilePath = "docs/Guide.md",
            StartLine = 1,
            EndLine = 3,
            RelativeProjectPath = (string?)null,
            DocumentKind = ApplicationDocumentKind.Markdown
        });
    }

    [Fact]
    public async Task WhenTargetNodeDoesNotExist_ThenItReturnsNull()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);
        var lookup = new RefactorTargetLookup(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        var result = await lookup.GetTarget(42, TestContext.Current.CancellationToken);

        result.Should().BeNull();
    }
}
