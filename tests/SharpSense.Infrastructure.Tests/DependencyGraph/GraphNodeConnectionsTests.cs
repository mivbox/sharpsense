using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Tests.DependencyGraph;

public sealed class GraphNodeConnectionsTests
{
    [Fact]
    public async Task WhenConnectionsArePaged_ThenAllDirectionsAndTypesAreGroupedAcrossPeers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory()).ReplaceWorkspace(Snapshot(610), ct);
        await using var context = await factory.GetContext(ct);
        var ids = await context.GraphNodes.ToDictionaryAsync(node => node.CanonicalId, node => node.Id, ct);
        var repository = Repository(context);
        SQLitePCL.raw.sqlite3_limit(
            factory.GetSqliteConnection().Handle!,
            SQLitePCL.raw.SQLITE_LIMIT_VARIABLE_NUMBER,
            64);
        var request = new GraphNodeConnectionsRequest(ids["code:root"], PageSize: 20, IncludeTotal: true);
        var first = await repository.GetNodeConnections(request, ct);
        var items = new List<GraphNodeConnection>(first.Items);
        var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var page = await repository.GetNodeConnections(
                request with
                {
                    Cursor = cursor,
                    PageSize = 500,
                    IncludeTotal = false
                },
                ct);
            page.Node.Should().Be(first.Node);
            page.Revision.Should().Be(first.Revision);
            page.TotalCount.Should().BeNull();
            items.AddRange(page.Items);
            cursor = page.NextCursor;
        }

        first.TotalCount.Should().Be(614);
        items.Count.Should().Be(first.TotalCount);
        items.Select(item => item.Node.Id)
            .Distinct()
            .Count().Should().Be(items.Count);
        items.Select(item => item.Node.Id).Should().Equal(items.Select(item => item.Node.Id)
            .Order());
        first.Node.Label.Should().Be("Fixture.Root");
        first.Node.CodeNodeId.Should().Be(first.Node.Id);
        var peer = items.Should().ContainSingle(item => item.Node.Id == ids["code:peer0"]).Which;
        peer.Relationships.Count.Should().Be(3);
        peer.Relationships.Should().Contain(edge => edge.Type == "methodcall" && edge.Direction == "outgoing" && edge.Metadata == "call");
        peer.Relationships.Should().Contain(edge => edge.Type == "instantiates" && edge.Direction == "outgoing");
        peer.Relationships.Should().Contain(edge => edge.Type == "fieldaccess" && edge.Direction == "incoming");
        items.Single(item => item.Node.Id == first.Node.Id).Relationships.Should().ContainSingle().Which.Direction.Should().Be("self");
        items.Should().Contain(item => item.Node.Type == "project" && item.Node.ProjectId == item.Node.Id);
        items.Where(item => item.Node.Label == "GET /api/items" && item.Node.Type == "http" && item.Node.CodeNodeId is null).Should().NotBeEmpty();
        items.Should().Contain(item => item.Node.Label == "react/useEffect" && item.Node.Type == "package");
    }

    [Theory]
    [InlineData("project:app", "project", "App")]
    [InlineData("http:GET:%2Fapi%2Fitems", "http", "GET /api/items")]
    [InlineData("package:react:useEffect", "package", "react/useEffect")]
    public async Task WhenInspectingAnyGraphNode_ThenDirectorySelectionIsOptional(string canonicalId, string type, string label)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory()).ReplaceWorkspace(Snapshot(2), ct);
        await using var context = await factory.GetContext(ct);
        var id = await context.GraphNodes.Where(node => node.CanonicalId == canonicalId)
            .Select(node => node.Id)
            .SingleAsync(ct);

        var page = await Repository(context).GetNodeConnections(new(id, IncludeTotal: true), ct);

        page.Node.Id.Should().Be(id);
        page.Node.Type.Should().Be(type);
        page.Node.Label.Should().Be(label);
        page.TotalCount.Should().Be(1);
        page.Items.Should().ContainSingle().Which.Node.Label.Should().Be("Fixture.Root");
    }

    [Fact]
    public async Task WhenInspectingConnections_ThenNodesWorkspaceCursorKindAndRevisionAreValidated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        var writer = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        await writer.ReplaceWorkspace(Snapshot(2), ct);
        await using var context = await factory.GetContext(ct);
        var ids = await context.GraphNodes.ToDictionaryAsync(node => node.CanonicalId, node => node.Id, ct);
        var repository = Repository(context);
        var request = new GraphNodeConnectionsRequest(ids["code:root"], PageSize: 1);
        var page = await repository.GetNodeConnections(request, ct);
        var next = request with
        {
            Cursor = page.NextCursor
        };

        await ((Func<Task>)(() => repository.GetNodeConnections(new(999_999), ct))).Should().ThrowExactlyAsync<KeyNotFoundException>();
        await ((Func<Task>)(() => repository.GetNodeConnections(new(0), ct))).Should().ThrowExactlyAsync<ArgumentException>();
        await ((Func<Task>)(() => repository.GetNodeConnections(
            request with
            {
                PageSize = 501
            },
            ct))).Should().ThrowExactlyAsync<ArgumentException>();
        await ((Func<Task>)(() => repository.GetNodeConnections(
            next with
            {
                NodeId = ids["code:peer0"]
            },
            ct))).Should().ThrowExactlyAsync<ArgumentException>();
        await ((Func<Task>)(() => Repository(context, "other").GetNodeConnections(next, ct))).Should().ThrowExactlyAsync<ArgumentException>();
        var directoryId = await context.Directories.Where(directory => directory.Path == "")
            .Select(directory => directory.Id)
            .SingleAsync(ct);
        var graphPage = await repository.GetNodesPage(new([directoryId], PageSize: 1), ct);
        await ((Func<Task>)(() => repository.GetNodeConnections(
            next with
            {
                Cursor = graphPage.NextCursor
            },
            ct))).Should().ThrowExactlyAsync<ArgumentException>();

        var isolated = await repository.GetNodeConnections(new(ids["code:isolated"], IncludeTotal: true), ct);
        isolated.Items.Should().BeEmpty();
        isolated.TotalCount.Should().Be(0);
        isolated.NextCursor.Should().BeNull();
        await writer.ReplaceWorkspace(Snapshot(3), ct);
        await ((Func<Task>)(() => repository.GetNodeConnections(next, ct))).Should().ThrowExactlyAsync<GraphRevisionChangedException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await ((Func<Task>)(() => repository.GetNodeConnections(request, cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
    }

    private static GraphPageRepository Repository(SharpSenseDbContext context, string name = "fixture")
        => new(
            context,
            new RepositoryWorkspace(
                "/repo",
                $"/workspace-home/{name}/index.db",
                new FileSystem()));

    private static ExtractedNodes Snapshot(int peerCount)
    {
        var nodes = Enumerable.Range(0, peerCount)
            .Select(index => Node(
                $"peer{index}",
                $"Fixture.Peer{index}"))
            .ToArray();
        var edges = nodes.Select(node => new IndexedDependency("code:root", node.CanonicalId, EdgeType.MethodCall, "call"))
            .ToList();
        edges.AddRange([
            new("code:root", "code:peer0", EdgeType.Instantiates),
            new("code:peer0", "code:root", EdgeType.FieldAccess),
            new("code:root", "code:root", EdgeType.MethodCall),
            new("project:app", "code:root", EdgeType.ParentOf),
            new("code:root", "http:GET:%2Fapi%2Fitems", EdgeType.HttpRequest, "/api/items"),
            new("code:root", "package:react:useEffect", EdgeType.Import)
        ]);

        return new(
            [new("project:app", "App", "App.csproj", "project")],
            [Node("root", "Fixture.Root"), Node("isolated", "Fixture.Isolated"), .. nodes],
            edges,
            []);
    }

    private static IndexedCodeNode Node(string id, string label)
        => new(
            $"code:{id}",
            "project:app",
            label,
            label,
            NodeType.Class,
            "src/App.cs",
            1,
            2,
            label,
            label,
            "body");
}
