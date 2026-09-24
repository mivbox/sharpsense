using System.Data.Common;
using System.IO.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.DependencyGraph;

public sealed class GraphPageRepositoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public async Task WhenFollowingCursors_ThenLoadsCompleteScopeWithoutDuplicatesOrTotalCaps(string directory)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        var writer = new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>());
        var graph = Snapshot(2_100);
        await writer.ReplaceTarget(graph, ct);
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var directoryId = await context.Directories.Where(item => item.Path == directory).Select(item => item.Id).SingleAsync(ct);
        var repository = new GraphPageRepository(context, Workspace());
        var request = new GraphPageRequest([directoryId], PageSize: 333, IncludeTotal: true);
        var first = await repository.GetNodesPage(request, ct);
        var nodes = new List<GraphPageNode>(first.Items);
        var cursor = first.NextCursor;
        Assert.NotNull(cursor);
        while (cursor is not null)
        {
            // Changing page size must not invalidate a cursor or cap the complete result.
            var page = await repository.GetNodesPage(request with { Cursor = cursor, PageSize = 5_000, IncludeTotal = false }, ct);
            Assert.Equal(first.Revision, page.Revision);
            Assert.Null(page.TotalCount);
            nodes.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        Assert.Equal(first.TotalCount, nodes.Count);
        Assert.True(nodes.Count > 1_000);
        Assert.Equal(nodes.Count, nodes.Select(node => node.Id).Distinct().Count());
        Assert.Equal(nodes.OrderBy(node => node.Scope == "selected" ? 0 : 1).ThenBy(node => node.Id), nodes);

        var identities = await context.GraphNodes.ToDictionaryAsync(node => node.CanonicalId, node => node.Id, ct);
        var selected = graph.CodeNodes.Where(node => directory == "" || node.RelativeFilePath.StartsWith(directory + "/", StringComparison.Ordinal))
            .Select(node => node.CanonicalId)
            .Concat(graph.Projects.Where(project => directory == "" || project.RelativeFilePath.StartsWith(directory + "/", StringComparison.Ordinal))
                .Select(project => project.Id)).ToHashSet();
        var expectedEdges = graph.Edges.Where(edge => selected.Contains(edge.CallerId) || selected.Contains(edge.CalleeId)).ToArray();
        var expectedNodes = selected.Concat(expectedEdges.SelectMany(edge => new[] { edge.CallerId, edge.CalleeId })).ToHashSet();
        Assert.Equal(expectedNodes.Select(id => identities[id]).Order(), nodes.Select(node => node.Id).Order());
        foreach (var canonicalId in expectedNodes)
        {
            var node = Assert.Single(nodes, node => node.Id == identities[canonicalId]);
            var code = graph.CodeNodes.SingleOrDefault(candidate => candidate.CanonicalId == canonicalId);
            Assert.Equal(code?.FullyQualifiedName ?? graph.Projects.Single(project => project.Id == canonicalId).Name, node.Label);
            Assert.Equal(selected.Contains(canonicalId) ? "selected" : "external", node.Scope);
            Assert.Equal(code is null ? (int?)null : identities[canonicalId], node.CodeNodeId);
        }

        var edgeRequest = request with { Revision = first.Revision, PageSize = 337 };
        var firstEdges = await repository.GetEdgesPage(edgeRequest, ct);
        var edges = new List<GraphPageEdge>(firstEdges.Items);
        cursor = firstEdges.NextCursor;
        while (cursor is not null)
        {
            var page = await repository.GetEdgesPage(edgeRequest with { Cursor = cursor, PageSize = 701, IncludeTotal = false }, ct);
            edges.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        Assert.Equal(firstEdges.TotalCount, edges.Count);
        Assert.True(edges.Count > 2_000);
        Assert.Equal(edges.Count, edges.Select(edge => (edge.Source, edge.Target, edge.Type)).Distinct().Count());
        Assert.Equal(expectedEdges.Select(edge => (identities[edge.CallerId], identities[edge.CalleeId], edge.EdgeType.ToString().ToLowerInvariant())).Order(),
            edges.Select(edge => (edge.Source, edge.Target, edge.Type)).Order());
        var nodeIds = nodes.Select(node => node.Id).ToHashSet();
        Assert.All(edges, edge => Assert.True(nodeIds.Contains(edge.Source) && nodeIds.Contains(edge.Target)));
    }

    [Fact]
    public async Task WhenSelectedPageEndsExactlyAtBoundary_ThenLoadsHttpAndPackageNodesOnFollowingPages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        var graph = Snapshot(10);
        graph = graph with
        {
            Edges = [.. graph.Edges,
                new("code:0", "http:GET:%2Fapi%2Fusers", EdgeType.HttpRequest, "/api/users"),
                new("code:0", "package:%40mui%2Fmaterial:Box", EdgeType.Import)]
        };
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>()).ReplaceTarget(graph, ct);
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var root = await context.Directories.Where(item => item.Path == "").Select(item => item.Id).SingleAsync(ct);
        var repository = new GraphPageRepository(context, Workspace());
        var selected = await repository.GetNodesPage(new([root], PageSize: 12, IncludeTotal: true), ct);
        Assert.Equal(14, selected.TotalCount);
        Assert.All(selected.Items, node => Assert.Equal("selected", node.Scope));

        var boundary = await repository.GetNodesPage(new([root], PageSize: 12, Cursor: selected.NextCursor), ct);

        Assert.Null(boundary.NextCursor);
        Assert.Equal(2, boundary.Items.Count);
        Assert.All(boundary.Items, node => Assert.Equal("external", node.Scope));
        Assert.Contains(boundary.Items, node => node.Label == "GET /api/users" && node.Type == "http");
        Assert.Contains(boundary.Items, node => node.Label == "@mui/material/Box" && node.Type == "package");
        var edges = await repository.GetEdgesPage(new([root]), ct);
        Assert.Contains(edges.Items, edge => edge.Type == "http-request" && edge.Scope == "boundary" && edge.Metadata == "/api/users");
    }

    [Fact]
    public async Task WhenGraphChanges_ThenRejectsOldCursorsButUnchangedReindexPreservesRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        var writer = new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>());
        var graph = Snapshot(10);
        await writer.ReplaceTarget(graph, ct);
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var root = await context.Directories.Where(item => item.Path == "").Select(item => item.Id).SingleAsync(ct);
        var repository = new GraphPageRepository(context, Workspace());
        var request = new GraphPageRequest([root], PageSize: 2);
        var first = await repository.GetNodesPage(request, ct);

        await writer.ReplaceTarget(graph, ct);
        var unchanged = await repository.GetNodesPage(request with { Cursor = first.NextCursor }, ct);
        Assert.Equal(first.Revision, unchanged.Revision);

        var changedNode = graph.CodeNodes[0] with { Summary = "Changed" };
        var changedNodes = graph.CodeNodes.Where(node => node.RelativeFilePath == changedNode.RelativeFilePath)
            .Select(node => node.CanonicalId == changedNode.CanonicalId ? changedNode : node).ToArray();
        var changedIds = changedNodes.Select(node => node.CanonicalId).ToHashSet();
        await writer.ReplaceWorkspaceFiles([changedNode.RelativeFilePath],
            new([], changedNodes, graph.Edges.Where(edge => changedIds.Contains(edge.CallerId)).ToArray(), []), ct);

        await Assert.ThrowsAsync<GraphRevisionChangedException>(
            () => repository.GetNodesPage(request with { Cursor = first.NextCursor }, ct));
        await Assert.ThrowsAsync<GraphRevisionChangedException>(
            () => repository.GetEdgesPage(request with { Revision = first.Revision }, ct));
        Assert.NotEqual(first.Revision, (await repository.GetNodesPage(request, ct)).Revision);
    }

    [Fact]
    public async Task WhenCursorBelongsToAnotherScopeWorkspaceOrPageType_ThenRejectsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>()).ReplaceTarget(Snapshot(10), ct);
        await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
        var root = await context.Directories.Where(item => item.Path == "").Select(item => item.Id).SingleAsync(ct);
        var child = await context.Directories.Where(item => item.Path == "A").Select(item => item.Id).SingleAsync(ct);
        var repository = new GraphPageRepository(context, Workspace());
        var request = new GraphPageRequest([root], PageSize: 2);
        var first = await repository.GetNodesPage(request, ct);
        var next = request with { Cursor = first.NextCursor };

        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetNodesPage(next with { DirectoryIds = [child] }, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetEdgesPage(next, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetNodesPage(next with { Cursor = "broken!" }, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => new GraphPageRepository(context, Workspace("other")).GetNodesPage(next, ct));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.GetNodesPage(request with { PageSize = 5_001 }, ct));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.GetNodesPage(request, cancelled.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenRevisionChangesDuringPageRead_ThenDiscardsMixedPage(bool connections)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory<SharpSenseDbContext>()).ReplaceTarget(Snapshot(10), ct);
        var options = new DbContextOptionsBuilder<SharpSenseDbContext>().UseSqlite(factory.GetSqliteConnection())
            .AddInterceptors(new ChangeRevisionAfterRead()).Options;
        await using var context = new SharpSenseDbContext(options);
        var root = await context.Directories.Where(item => item.Path == "").Select(item => item.Id).SingleAsync(ct);
        var nodeId = await context.CodeNodes.Select(node => node.Id).FirstAsync(ct);

        await Assert.ThrowsAsync<GraphRevisionChangedException>(
            async () =>
            {
                var repository = new GraphPageRepository(context, Workspace());
                if (connections)
                {
                    await repository.GetNodeConnections(new(nodeId), ct);
                }
                else
                {
                    await repository.GetNodesPage(new([root]), ct);
                }
            });
    }

    private sealed class ChangeRevisionAfterRead : DbCommandInterceptor
    {
        private bool _changed;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!_changed && command.CommandText.Contains("GraphRevision", StringComparison.Ordinal))
            {
                _changed = true;
                await using var mutation = command.Connection!.CreateCommand();
                mutation.CommandText = "UPDATE IndexRunState SET GraphRevision = 'concurrent-change' WHERE Id = 1";
                await mutation.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    private static RepositoryWorkspace Workspace(string name = "fixture")
        => new("/repo", $"/workspace-home/{name}/index.db", new FileSystem());

    private static ExtractedNodes Snapshot(int count)
    {
        var nodes = Enumerable.Range(0, count).Select(index =>
        {
            var project = index < count / 2 ? "A" : "B";
            return new IndexedCodeNode($"code:{index}", $"project:{project}", $"Fixture.Node{index}", $"Node{index}",
                NodeType.Class, $"{project}/Node.cs", 1, 3, "Node summary", $"Node{index}", "original");
        }).ToArray();
        var edges = Enumerable.Range(0, count).SelectMany(index => new[]
        {
            new IndexedDependency(nodes[index].CanonicalId, nodes[(index + 1) % count].CanonicalId, EdgeType.MethodCall),
            new IndexedDependency(nodes[index].CanonicalId, nodes[(index + 1) % count].CanonicalId, EdgeType.Implements)
        }).ToArray();
        return new([new("project:A", "A", "A/A.csproj", "A"), new("project:B", "B", "B/B.csproj", "B")],
            nodes, [.. edges, new("project:A", "project:B", EdgeType.ProjectReference)], []);
    }

}
