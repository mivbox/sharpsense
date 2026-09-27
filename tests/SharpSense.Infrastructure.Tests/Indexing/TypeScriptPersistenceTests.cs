using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class TypeScriptPersistenceTests
{
    [Theory]
    [InlineData("http:GET:%2Fapi%2Fusers", EdgeType.HttpRequest, nameof(GraphNodeKind.Http), "/api/users")]
    [InlineData("package:%40mui%2Fmaterial:Box", EdgeType.Import, nameof(GraphNodeKind.Package), null)]
    public async Task WhenWorkspaceCallersShareSyntheticTarget_ThenRetainsIdentityUntilLastReferenceRemoved(
        string target, EdgeType edgeType, string expectedKind, string? metadata)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var first = Node("First", "src/First.ts");
        var second = Node("Second", "src/Second.ts");
        await repository.ReplaceWorkspace(
            new(
                [],
                [first, second],
                [new(first.CanonicalId, target, edgeType, metadata), new(second.CanonicalId, target, edgeType, metadata)],
                []),
            ct);
        await using var db = await factory.GetContext(ct);
        var targetId = await db.GraphNodes.Where(node => node.CanonicalId == target)
            .Select(node => node.Id)
            .SingleAsync(ct);

        // Updating an existing shared target must not attempt a duplicate graph-node insert.
        await repository.ReplaceWorkspace(
            new(
                [],
                [first with
                {
                    Summary = "changed"
                }, second],
                [new(first.CanonicalId, target, edgeType, metadata), new(second.CanonicalId, target, edgeType, metadata)],
                []),
            ct);
        db.ChangeTracker.Clear();
        (await db.GraphNodes.SingleAsync(node => node.CanonicalId == target, ct)).Id.Should().Be(targetId);
        (await db.GraphNodes.SingleAsync(node => node.Id == targetId, ct)).Kind.ToString().Should().Be(expectedKind);
        (await db.DependencyEdges.CountAsync(edge => edge.CalleeNodeId == targetId, ct)).Should().Be(2);

        await repository.ReplaceWorkspace(new([], [first, second], [new(second.CanonicalId, target, edgeType, metadata)], []), ct);
        (await db.GraphNodes.AnyAsync(node => node.Id == targetId, ct)).Should().BeTrue();
        (await db.DependencyEdges.CountAsync(edge => edge.CalleeNodeId == targetId, ct)).Should().Be(1);

        await repository.ReplaceWorkspace(new([], [first, second], [], []), ct);
        (await db.GraphNodes.AnyAsync(node => node.Id == targetId, ct)).Should().BeFalse();
        (await db.CodeNodes.CountAsync(ct)).Should().Be(2);
    }

    [Theory]
    [InlineData("http:GET:%2Fapi%2Fusers", EdgeType.HttpRequest, "GET /api/users", "http-request")]
    [InlineData("package:%40mui%2Fmaterial:Box", EdgeType.Import, "@mui/material/Box", "import")]
    public async Task WhenDependencyMetadataChanges_ThenWorkspacePersistenceAndGraphExposeLatestMetadata(
        string target, EdgeType edgeType, string expectedLabel, string expectedType)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var caller = Node("loadUsers", "src/client.ts");
        ExtractedNodes Snapshot(string metadata) => new(
            [],
            [caller],
            [new(caller.CanonicalId, target, edgeType, metadata)],
            []);
        await repository.ReplaceWorkspace(Snapshot("first"), ct);
        await repository.ReplaceWorkspace(Snapshot("second"), ct);
        await using var db = await factory.GetContext(ct);
        (await db.DependencyEdges.SingleAsync(ct)).Metadata.Should().Be("second");
        await repository.ReplaceWorkspace(Snapshot("third"), ct);
        db.ChangeTracker.Clear();
        (await db.DependencyEdges.SingleAsync(ct)).Metadata.Should().Be("third");

        var directoryId = await db.Directories.Where(directory => directory.Path == "src")
            .Select(directory => directory.Id)
            .SingleAsync(ct);
        var graph = new GraphPageRepository(db, new RepositoryWorkspace("/repo", "/workspace-home/fixture/index.db", new FileSystem()));
        var nodes = (await graph.GetNodesPage(new([directoryId]), ct)).Items;
        var edges = (await graph.GetEdgesPage(new([directoryId]), ct)).Items;
        var targetId = await db.GraphNodes.Where(node => node.CanonicalId == target)
            .Select(node => node.Id)
            .SingleAsync(ct);
        var boundary = nodes.Single(node => node.Id == targetId);
        boundary.Label.Should().Be(expectedLabel);
        boundary.Scope.Should().Be("external");
        boundary.CodeNodeId.Should().BeNull();
        boundary.IsClickable.Should().BeFalse();
        edges.Should().ContainSingle();
        edges[0].Type.Should().Be(expectedType);
        edges[0].Metadata.Should().Be("third");
        edges[0].Scope.Should().Be("boundary");
    }

    [Fact]
    public async Task WhenFullReindexChangesSyntheticDependencies_ThenRetainsCodeIdentityAndAuthoredMemory()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var caller = Node("App", "src/App.tsx") with
        {
            NodeType = NodeType.Component
        };
        await repository.ReplaceWorkspace(
            new(
                [],
                [caller],
                [new(caller.CanonicalId, "package:react:useEffect", EdgeType.Import)],
                []),
            ct);
        await using var db = await factory.GetContext(ct);
        var originalId = await db.CodeNodes.Select(node => node.Id)
            .SingleAsync(ct);
        var memoryId = Guid.NewGuid();
        db.MemoryNodes.Add(new MemoryNodeRecord
        {
            Id = memoryId,
            TargetCodeNodeId = originalId,
            TargetCodeHash = "body",
            Content = "Keep this authored context",
            ContentHash = "memory",
            TagsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);

        await repository.ReplaceWorkspace(
            new(
                [],
                [caller],
                [new(caller.CanonicalId, "http:POST:%2Fapi%2Fsave", EdgeType.HttpRequest, "/api/save")],
                []),
            ct);
        db.ChangeTracker.Clear();
        (await db.CodeNodes.SingleAsync(ct)).Id.Should().Be(originalId);
        (await db.MemoryNodes.SingleAsync(ct)).Id.Should().Be(memoryId);
        (await db.MemoryNodes.SingleAsync(ct)).Content.Should().Be("Keep this authored context");
        (await db.GraphNodes.AnyAsync(node => node.CanonicalId == "package:react:useEffect", ct)).Should().BeFalse();
        (await db.GraphNodes.CountAsync(node => node.Kind == GraphNodeKind.Http, ct)).Should().Be(1);
    }

    private static IndexedCodeNode Node(string name, string path)
        => new(
            $"code:ts:{path}:{name}",
            null,
            $"ts:{path}::{name}",
            name,
            NodeType.Method,
            path,
            1,
            3,
            name,
            name,
            "body");
}
