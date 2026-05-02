using AwesomeAssertions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Refactoring;
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
            Kind = DocumentKind.Source
        });
        context.GraphNodes.Add(new GraphNodeRecord
        {
            Id = 42,
            CanonicalId = "code:project-app:Fixture.App.Feature.Run()",
            Kind = GraphNodeKind.Code
        });
        context.CodeNodes.Add(new CodeNodeRecord
        {
            Id = 42,
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
            EndLine = 18
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
