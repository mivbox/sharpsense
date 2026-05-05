using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;
using System.Reflection;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class KnowledgeGraphRepositoryTests
{
    [Fact]
    public async Task WhenReplacingTargetWithMissingProjectReference_ThenPersistsNullProjectNodeId()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        await repository.ReplaceTarget(
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

        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
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
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());

        await repository.ReplaceTarget(
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
        var canonicalizeSnapshot = typeof(KnowledgeGraphRepository)
            .GetMethod("CanonicalizeSnapshot", BindingFlags.NonPublic | BindingFlags.Static);
        var areEquivalentSnapshots = typeof(KnowledgeGraphRepository)
            .GetMethod("AreEquivalentSnapshots", BindingFlags.NonPublic | BindingFlags.Static);

        canonicalizeSnapshot.Should().NotBeNull();
        areEquivalentSnapshots.Should().NotBeNull();

        var canonicalSnapshot = (ExtractedNodes?)canonicalizeSnapshot!
            .Invoke(null, [equivalentSnapshot, null]);
        var areEquivalent = (bool?)areEquivalentSnapshots!
            .Invoke(null, [persistedSnapshot, canonicalSnapshot!]);

        canonicalSnapshot.Should().NotBeNull();
        areEquivalent.Should().BeTrue();
    }

    [Fact]
    public async Task WhenCanonicalizingIncrementalGraph_ThenItPreservesKnownCrossFileEdgesAndCanonicalOrdering()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>());
        const string projectId = "project:fixture";
        const string processorPath = "src/Fixture/Processor.cs";
        const string helperPath = "src/Fixture/Helper.cs";
        const string helperId = "code:fixture:src/Fixture/Helper.cs:M:Fixture.Helper.Run()";
        const string runId = "code:fixture:src/Fixture/Processor.cs:M:Fixture.Processor.Run()";
        const string alphaId = "code:fixture:src/Fixture/Processor.cs:M:Fixture.Processor.Alpha()";

        await repository.ReplaceTarget(
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
                        helperId,
                        projectId,
                        "Fixture.Helper.Run()",
                        "Helper.Run()",
                        NodeType.Method,
                        helperPath,
                        10,
                        18,
                        "Runs the helper.",
                        "Helper.Run()\nRuns the helper.",
                        "helper-hash"),
                    new IndexedCodeNode(
                        runId,
                        projectId,
                        "Fixture.Processor.Run()",
                        "Processor.Run()",
                        NodeType.Method,
                        processorPath,
                        10,
                        18,
                        "Runs the processor.",
                        "Processor.Run()\nRuns the processor.",
                        "run-hash")
                ],
                [
                    new IndexedDependency(
                        runId,
                        helperId,
                        EdgeType.MethodCall)
                ],
                []),
            TestContext.Current.CancellationToken);

        await repository.ReplaceWorkspaceFiles(
            [processorPath],
            new ExtractedNodes(
                [],
                [
                    new IndexedCodeNode(
                        runId,
                        projectId,
                        "Fixture.Processor.Run()",
                        "Processor.Run()",
                        NodeType.Method,
                        processorPath,
                        10,
                        18,
                        "Runs the processor.",
                        "Processor.Run()\nRuns the processor.",
                        "run-hash"),
                    new IndexedCodeNode(
                        alphaId,
                        projectId,
                        "Fixture.Processor.Alpha()",
                        "Processor.Alpha()",
                        NodeType.Method,
                        processorPath,
                        20,
                        28,
                        "Runs alpha.",
                        "Processor.Alpha()\nRuns alpha.",
                        "alpha-hash")
                ],
                [
                    new IndexedDependency(
                        runId,
                        helperId,
                        EdgeType.MethodCall),
                    new IndexedDependency(
                        alphaId,
                        helperId,
                        EdgeType.MethodCall)
                ],
                []),
            TestContext.Current.CancellationToken);

        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var loadWorkspaceFilesSnapshot = typeof(KnowledgeGraphRepository)
            .GetMethod("LoadWorkspaceFilesSnapshot", BindingFlags.NonPublic | BindingFlags.Static);
        var canonicalizeSnapshot = typeof(KnowledgeGraphRepository)
            .GetMethod("CanonicalizeSnapshot", BindingFlags.NonPublic | BindingFlags.Static);
        var areEquivalentSnapshots = typeof(KnowledgeGraphRepository)
            .GetMethod("AreEquivalentSnapshots", BindingFlags.NonPublic | BindingFlags.Static);

        loadWorkspaceFilesSnapshot.Should().NotBeNull();
        canonicalizeSnapshot.Should().NotBeNull();
        areEquivalentSnapshots.Should().NotBeNull();

        var persistedSnapshotTask = (Task<ExtractedNodes>?)loadWorkspaceFilesSnapshot!
            .Invoke(null, [context, (IReadOnlyList<string>)[processorPath], TestContext.Current.CancellationToken]);
        persistedSnapshotTask.Should().NotBeNull();
        var persistedSnapshot = await persistedSnapshotTask!;
        var knownNodeIds = new HashSet<string>(StringComparer.Ordinal)
        {
            projectId,
            helperId,
            runId,
            alphaId
        };
        var equivalentSnapshot = new ExtractedNodes(
            [],
            [
                new IndexedCodeNode(
                    runId,
                    projectId,
                    "Fixture.Processor.Run()",
                    "Processor.Run()",
                    NodeType.Method,
                    processorPath,
                    10,
                    18,
                    "Runs the processor.",
                    "Processor.Run()\nRuns the processor.",
                    "run-hash"),
                new IndexedCodeNode(
                    alphaId,
                    projectId,
                    "Fixture.Processor.Alpha()",
                    "Processor.Alpha()",
                    NodeType.Method,
                    processorPath,
                    20,
                    28,
                    "Runs alpha.",
                    "Processor.Alpha()\nRuns alpha.",
                    "alpha-hash")
            ],
            [
                new IndexedDependency(
                    runId,
                    helperId,
                    EdgeType.MethodCall),
                new IndexedDependency(
                    alphaId,
                    helperId,
                    EdgeType.MethodCall)
            ],
            []);
        var canonicalSnapshot = (ExtractedNodes?)canonicalizeSnapshot!
            .Invoke(null, [equivalentSnapshot, knownNodeIds]);
        var areEquivalent = (bool?)areEquivalentSnapshots!
            .Invoke(null, [persistedSnapshot, canonicalSnapshot!]);

        canonicalSnapshot.Should().NotBeNull();
        areEquivalent.Should().BeTrue();
    }
}
