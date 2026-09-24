using System.IO.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.DependencyGraph;

public sealed class GraphNodeConnectionsTests
{
    [Fact]
    public async Task ConnectionsGroupEveryDirectionAndTypeAcrossCompletePeerPages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>()).ReplaceTarget(Snapshot(610), ct);
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var ids = await context.GraphNodes.ToDictionaryAsync(node => node.CanonicalId, node => node.Id, ct);
        var repository = Repository(context);
        SQLitePCL.raw.sqlite3_limit(factory.GetSqliteConnection().Handle!, SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER, 64);
        var request = new GraphNodeConnectionsRequest(ids["code:root"], PageSize: 20, IncludeTotal: true);
        var first = await repository.GetNodeConnections(request, ct);
        var items = new List<GraphNodeConnection>(first.Items);
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var page = await repository.GetNodeConnections(request with { Cursor = cursor, PageSize = 500, IncludeTotal = false }, ct);
            Assert.Equal(first.Node, page.Node);
            Assert.Equal(first.Revision, page.Revision);
            Assert.Null(page.TotalCount);
            items.AddRange(page.Items);
            cursor = page.NextCursor;
        }

        Assert.Equal(614, first.TotalCount);
        Assert.Equal(first.TotalCount, items.Count);
        Assert.Equal(items.Count, items.Select(item => item.Node.Id).Distinct().Count());
        Assert.Equal(items.Select(item => item.Node.Id).Order(), items.Select(item => item.Node.Id));
        Assert.Equal("Fixture.Root", first.Node.Label);
        Assert.Equal(first.Node.Id, first.Node.CodeNodeId);
        var peer = Assert.Single(items, item => item.Node.Id == ids["code:peer0"]);
        Assert.Equal(3, peer.Relationships.Count);
        Assert.Contains(peer.Relationships, edge => edge.Type == "methodcall" && edge.Direction == "outgoing" && edge.Metadata == "call");
        Assert.Contains(peer.Relationships, edge => edge.Type == "instantiates" && edge.Direction == "outgoing");
        Assert.Contains(peer.Relationships, edge => edge.Type == "fieldaccess" && edge.Direction == "incoming");
        Assert.Equal("self", Assert.Single(items.Single(item => item.Node.Id == first.Node.Id).Relationships).Direction);
        Assert.Contains(items, item => item.Node.Type == "project" && item.Node.ProjectId == item.Node.Id);
        Assert.Contains(items, item => item.Node.Label == "GET /api/items" && item.Node.Type == "http" && item.Node.CodeNodeId is null);
        Assert.Contains(items, item => item.Node.Label == "react/useEffect" && item.Node.Type == "package");
    }

    [Theory]
    [InlineData("project:app", "project", "App")]
    [InlineData("http:GET:%2Fapi%2Fitems", "http", "GET /api/items")]
    [InlineData("package:react:useEffect", "package", "react/useEffect")]
    public async Task AnyGraphNodeCanBeInspectedWithoutDirectorySelection(string canonicalId, string type, string label)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>()).ReplaceTarget(Snapshot(2), ct);
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var id = await context.GraphNodes.Where(node => node.CanonicalId == canonicalId).Select(node => node.Id).SingleAsync(ct);

        var page = await Repository(context).GetNodeConnections(new(id, IncludeTotal: true), ct);

        Assert.Equal(id, page.Node.Id);
        Assert.Equal(type, page.Node.Type);
        Assert.Equal(label, page.Node.Label);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Fixture.Root", Assert.Single(page.Items).Node.Label);
    }

    [Fact]
    public async Task ConnectionsValidateMissingNodesWorkspaceCursorKindAndRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        var writer = new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>());
        await writer.ReplaceTarget(Snapshot(2), ct);
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var ids = await context.GraphNodes.ToDictionaryAsync(node => node.CanonicalId, node => node.Id, ct);
        var repository = Repository(context);
        var request = new GraphNodeConnectionsRequest(ids["code:root"], PageSize: 1);
        var page = await repository.GetNodeConnections(request, ct);
        var next = request with { Cursor = page.NextCursor };

        await Assert.ThrowsAsync<KeyNotFoundException>(() => repository.GetNodeConnections(new(999_999), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetNodeConnections(new(0), ct));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetNodeConnections(request with { PageSize = 501 }, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetNodeConnections(next with { NodeId = ids["code:peer0"] }, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Repository(context, "other").GetNodeConnections(next, ct));
        var directoryId = await context.Directories.Where(directory => directory.Path == "").Select(directory => directory.Id).SingleAsync(ct);
        var graphPage = await repository.GetNodesPage(new([directoryId], PageSize: 1), ct);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetNodeConnections(next with { Cursor = graphPage.NextCursor }, ct));

        var isolated = await repository.GetNodeConnections(new(ids["code:isolated"], IncludeTotal: true), ct);
        Assert.Empty(isolated.Items);
        Assert.Equal(0, isolated.TotalCount);
        Assert.Null(isolated.NextCursor);
        await writer.ReplaceTarget(Snapshot(3), ct);
        await Assert.ThrowsAsync<GraphRevisionChangedException>(() => repository.GetNodeConnections(next, ct));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetNodeConnections(request, cancelled.Token));
    }

    private static GraphPageRepository Repository(SharpSenseDbContext context, string name = "fixture")
        => new(context, new RepositoryWorkspace("/repo", $"/workspace-home/{name}/index.db", new FileSystem()));

    private static ExtractedNodes Snapshot(int peerCount)
    {
        var nodes = Enumerable.Range(0, peerCount).Select(index => Node($"peer{index}", $"Fixture.Peer{index}")).ToArray();
        var edges = nodes.Select(node => new IndexedDependency("code:root", node.CanonicalId, EdgeType.MethodCall, "call")).ToList();
        edges.AddRange([
            new("code:root", "code:peer0", EdgeType.Instantiates),
            new("code:peer0", "code:root", EdgeType.FieldAccess),
            new("code:root", "code:root", EdgeType.MethodCall),
            new("project:app", "code:root", EdgeType.ParentOf),
            new("code:root", "http:GET:%2Fapi%2Fitems", EdgeType.HttpRequest, "/api/items"),
            new("code:root", "package:react:useEffect", EdgeType.Import)
        ]);
        return new([new("project:app", "App", "App.csproj", "project")],
            [Node("root", "Fixture.Root"), Node("isolated", "Fixture.Isolated"), .. nodes], edges, []);
    }

    private static IndexedCodeNode Node(string id, string label)
        => new($"code:{id}", "project:app", label, label, NodeType.Class, "src/App.cs", 1, 2, label, label, "body");
}
