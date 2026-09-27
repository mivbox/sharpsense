using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class KnowledgeGraphRepositoryTests
{
    [Fact]
    public async Task WhenReplacingTargetWithMissingProjectReference_ThenPersistsNullProjectNodeId()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(inMemoryFactory.CreateDbContextFactory());

        await repository.ReplaceWorkspace(
            new ExtractedNodes(
                [],
                [
                new IndexedCodeNode(
                    "code:fixture:src/Fixture/Orphan.cs:T:Orphan",
                    "project:missing",
                    "Fixture.Orphan",
                    "Orphan",
                    NodeType.Class,
                    "src/Fixture/Orphan.cs",
                    1,
                    12,
                    "Orphan node.",
                    "Orphan\nOrphan node.")
                ],
                [],
                []),
            TestContext.Current.CancellationToken);

        await using var context = await inMemoryFactory.GetContext(
            ct: TestContext.Current.CancellationToken);
        var persistedNode = await context.CodeNodes
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        persistedNode.ProjectNodeId.Should().BeNull();
        persistedNode.FullyQualifiedName.Should().Be("Fixture.Orphan");
    }

    [Fact]
    public async Task WhenGettingPersistedCodeNodes_ThenItReturnsSearchTextAndBodyHash()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(inMemoryFactory.CreateDbContextFactory());

        await repository.ReplaceWorkspace(
            new ExtractedNodes(
                [],
                [
                    new IndexedCodeNode(
                        "code:fixture:src/Fixture/Processor.cs:M:Processor.Run",
                        "project:fixture",
                        "Fixture.Processor.Run()",
                        "Processor.Run()",
                        NodeType.Method,
                        "src/Fixture/Processor.cs",
                        10,
                        18,
                        "Runs the processor.",
                        "Processor.Run()\nRuns the processor.",
                        "hash-1",
                        [1f, 2f])
                ],
                [],
                []),
            TestContext.Current.CancellationToken);

        var persistedCodeNodes = await repository.GetPersistedCodeNodes(TestContext.Current.CancellationToken);
        var persistedCodeNode = persistedCodeNodes.Should().ContainSingle().Subject;

        persistedCodeNode.SearchText.Should().Be("Processor.Run()\nRuns the processor.");
        persistedCodeNode.BodyHash.Should().Be("hash-1");
        persistedCodeNode.VectorEmbedding.Should().Equal(1f, 2f);
    }

    [Fact]
    public void WhenCanonicalizingEquivalentGraph_ThenItMatchesThePersistedSnapshot()
    {
        var persistedSnapshot = new ExtractedNodes(
            [
                new IndexedProject(
                    "project:fixture",
                    "Fixture",
                    "src/Fixture/Fixture.csproj",
                    "project-hash")
            ],
            [
                new IndexedCodeNode(
                    "code:fixture:src/Fixture/Processor.cs:M:Fixture.Processor.Execute()",
                    "project:fixture",
                    "Fixture.Processor.Execute()",
                    "Processor.Execute()",
                    NodeType.Method,
                    "src/Fixture/Processor.cs",
                    20,
                    28,
                    "Executes the processor.",
                    "Processor.Execute()\nExecutes the processor.",
                    "execute-body-hash"),
                new IndexedCodeNode(
                    "code:fixture:src/Fixture/Processor.cs:M:Fixture.Processor.Run()",
                    "project:fixture",
                    "Fixture.Processor.Run()",
                    "Processor.Run()",
                    NodeType.Method,
                    "src/Fixture/Processor.cs",
                    10,
                    18,
                    "Runs the processor.",
                    "Processor.Run()\nRuns the processor.",
                    "body-hash")
            ],
            [
                new IndexedDependency(
                    "code:fixture:src/Fixture/Processor.cs:M:Fixture.Processor.Run()",
                    "code:fixture:src/Fixture/Processor.cs:M:Fixture.Processor.Execute()",
                    EdgeType.MethodCall)
            ],
            []);
        var equivalentSnapshot = new ExtractedNodes(
            [persistedSnapshot.Projects[0]],
            [persistedSnapshot.CodeNodes[1], persistedSnapshot.CodeNodes[0]],
            [
                new IndexedDependency(
                    "code:fixture:src/Fixture/Processor.cs:M:Fixture.Processor.Run()",
                    "code:fixture:src/Fixture/Processor.cs:M:Fixture.External()",
                    EdgeType.MethodCall),
                persistedSnapshot.Edges[0],
                persistedSnapshot.Edges[0]
            ],
            []);
        var canonicalSnapshot = GraphSnapshot.CanonicalizeSnapshot(equivalentSnapshot);

        GraphSnapshot.AreEquivalentSnapshots(persistedSnapshot, canonicalSnapshot).Should().BeTrue();
    }

    [Fact]
    public async Task WhenReplacingTargetWithDistinctIdentitiesAndMatchingNames_ThenPersistsBoth()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(inMemoryFactory.CreateDbContextFactory());
        const string projectId = "project:fixture";
        const string processorPath = "src/Fixture/Processor.Part1.cs";
        const string processorPart2Path = "src/Fixture/Processor.Part2.cs";
        const string firstCanonicalId = "code:fixture:src/Fixture/Processor.Part1.cs:T:Fixture.Processor";
        const string secondCanonicalId = "code:fixture:src/Fixture/Processor.Part2.cs:T:Fixture.Processor";

        await repository.ReplaceWorkspace(
            new ExtractedNodes(
                [
                    new IndexedProject(
                        projectId,
                        "Fixture",
                        "src/Fixture/Fixture.csproj",
                        "project-hash")
                ],
                [
                    new IndexedCodeNode(
                        firstCanonicalId,
                        projectId,
                        "Fixture.Processor",
                        "Processor",
                        NodeType.Class,
                        processorPath,
                        1,
                        20,
                        "First half of the processor.",
                        "Processor\nFirst half of the processor.",
                        "part-1-hash"),
                    new IndexedCodeNode(
                        secondCanonicalId,
                        projectId,
                        "Fixture.Processor",
                        "Processor",
                        NodeType.Class,
                        processorPart2Path,
                        1,
                        15,
                        "Second half of the processor.",
                        "Processor\nSecond half of the processor.",
                        "part-2-hash")
                ],
                [],
                []),
            TestContext.Current.CancellationToken);

        var persistedCodeNodes = await repository.GetPersistedCodeNodes(TestContext.Current.CancellationToken);
        persistedCodeNodes.Should().HaveCount(2);
        persistedCodeNodes.Select(node => node.CanonicalId)
            .Should().BeEquivalentTo(firstCanonicalId, secondCanonicalId);
        persistedCodeNodes.Should().OnlyContain(node => node.FullyQualifiedName == "Fixture.Processor");
    }
}
