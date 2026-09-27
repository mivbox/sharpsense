using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.EntityFrameworkCore;
using Moq;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.CodeAnalysis;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class MemoryIdentityRegressionTests
{
    [Fact]
    public async Task WhenGenericParameterIsRenamed_ThenReindexRetainsNumericIdentityAndMemoriesWithCurrentNames()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true));
        var dbFactory = factory.CreateDbContextFactory();
        var repository = new KnowledgeGraphRepository(dbFactory);
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "App",
            "App",
            LanguageNames.CSharp,
            filePath: "/repo/App.csproj"))
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(
            documentId,
            "Widget.cs",
            SourceText.From("public class Widget<T> { public T Echo(T value) => value; }"),
            filePath: "/repo/Widget.cs");
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/App.csproj"] = new("<Project Sdk=\"Microsoft.NET.Sdk\" />")
        });
        var repositoryWorkspace = new RepositoryWorkspace("/repo", "/repo/index.db", fileSystem);
        var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);
        var initial = await engine.Extract("/repo/App.csproj", solution, repositoryWorkspace, ct: ct);
        await repository.ReplaceWorkspace(Map(initial), ct);

        await using var db = await factory.GetContext(ct);
        var originalIds = await db.CodeNodes.Join(
            db.GraphNodes,
            node => node.Id,
            graph => graph.Id,
            (node, graph) => new
            {
                graph.CanonicalId,
                node.Id
            })
            .ToDictionaryAsync(node => node.CanonicalId, node => node.Id, ct);
        var originalNames = await db.CodeNodes.ToDictionaryAsync(node => node.Id, node => node.FullyQualifiedName, ct);
        originalNames.Values.Should().Contain("Widget<T>");
        originalNames.Count.Should().Be(2);

        var embeddings = new Mock<IEmbeddingGenerator>();
        embeddings.Setup(generator => generator.Generate(It.IsAny<string>(), ct))
            .ReturnsAsync((string content, CancellationToken _) => new TextEmbedding(content, [1f, 0f]));
        var store = new MemoryStore(dbFactory, embeddings.Object);
        foreach (var nodeId in originalIds.Values)
        {
            (await store.AttachMemory(
                nodeId,
                $"Authored context for {nodeId}",
                ["identity"],
                MemoryIntent.Invariant,
                ct))
                .IsSuccess.Should().BeTrue();
        }
        var originalMemories = await db.MemoryNodes.AsNoTracking()
            .ToArrayAsync(ct);

        solution = solution.WithDocumentText(
            documentId,
            SourceText.From("public class Widget<U> { public U Echo(U value) => value; }"));
        var changed = await engine.Extract("/repo/App.csproj", solution, repositoryWorkspace, ct: ct);
        changed.CodeNodes.Select(node => node.CanonicalId).Should().BeEquivalentTo(initial.CodeNodes.Select(node => node.CanonicalId));
        await repository.ReplaceWorkspace(Map(changed), ct);

        db.ChangeTracker.Clear();
        var currentIds = await db.CodeNodes.Join(
            db.GraphNodes,
            node => node.Id,
            graph => graph.Id,
            (node, graph) => new
            {
                graph.CanonicalId,
                node.Id
            })
            .ToDictionaryAsync(node => node.CanonicalId, node => node.Id, ct);
        currentIds.Should().BeEquivalentTo(originalIds);
        var currentNames = await db.CodeNodes.ToDictionaryAsync(node => node.Id, node => node.FullyQualifiedName, ct);
        currentNames.Values.Should().Contain("Widget<U>");
        currentNames.Values.Should().NotIntersectWith(originalNames.Values);
        (await db.MemoryNodes.AsNoTracking()
            .ToArrayAsync(ct)).Should().BeEquivalentTo(originalMemories);

        var memoriesByNode = await store.GetNodeMemories(originalIds.Values.ToArray(), [MemoryIntent.Invariant], ct);
        var memoriesById = await store.GetMemories(
            originalMemories.Select(memory => memory.Id)
                .ToArray(),
            ct);
        foreach (var memory in originalMemories)
        {
            var single = await store.GetMemory(memory.Id, ct);
            single.Should().NotBeNull();
            single!.TargetFullyQualifiedName.Should().Be(currentNames[memory.TargetCodeNodeId]);
            single.Content.Should().Be(memory.Content);
            single.IsStale.Should().BeTrue();
            memoriesById[memory.Id].Should().BeEquivalentTo(single);
            memoriesByNode[memory.TargetCodeNodeId].Should().ContainSingle().Which.Should().BeEquivalentTo(single);
        }
        (await db.Database.SqlQuery<string>($"SELECT FullyQualifiedName AS Value FROM CodeNodeSearch")
            .ToArrayAsync(ct))
            .Should().BeEquivalentTo(currentNames.Values);

        // Stable names and IDs remain idempotent on another full parse.
        await repository.ReplaceWorkspace(Map(await engine.Extract("/repo/App.csproj", solution, repositoryWorkspace, ct: ct)), ct);
        (await db.MemoryNodes.AsNoTracking()
            .ToArrayAsync(ct)).Should().BeEquivalentTo(originalMemories);
        await repository.ReplaceWorkspace(new([], [], [], []), ct);
        (await db.MemoryNodes.CountAsync(ct)).Should().Be(0);
    }

    private static ExtractedNodes Map(KnowledgeGraphExtractionPayload payload)
        => new(
            payload.Projects.Select(project => new IndexedProject(
                project.Id,
                project.Name,
                project.RelativeFilePath,
                project.ContentHash))
                .ToArray(),
            payload.CodeNodes.Select(node => new IndexedCodeNode(
                node.CanonicalId,
                node.ProjectId,
                node.FullyQualifiedName,
                node.DisplayName,
                node.NodeType,
                node.RelativeFilePath,
                node.StartLine,
                node.EndLine,
                node.Summary,
                node.SearchText,
                node.BodyHash))
                .ToArray(),
            payload.Edges.Select(edge => new IndexedDependency(edge.CallerId, edge.CalleeId, edge.EdgeType))
                .ToArray(),
            []);
}
