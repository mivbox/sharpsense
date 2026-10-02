using AwesomeAssertions;
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
using System.Data.Common;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Tests.DependencyGraph;

public sealed class GraphPageRepositoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("A")]
    public async Task WhenFollowingCursors_ThenLoadsCompleteScopeWithoutDuplicatesOrTotalCaps(string directory)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        var writer = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var graph = Snapshot(2_100);
        await writer.ReplaceWorkspace(graph, ct);
        await using var context = await factory.GetContext(ct);
        var directoryId = await context.Directories
            .Where(item => item.Path == directory)
            .Select(item => item.Id)
            .SingleAsync(ct);
        var repository = new GraphPageRepository(context, Workspace());
        var request = new GraphPageRequest([directoryId], PageSize: 333, IncludeTotal: true);
        var first = await repository.GetNodesPage(request, ct);
        var nodes = new List<GraphPageNode>(first.Items);
        var cursor = first.NextCursor;
        cursor.Should().NotBeNull();
        while (cursor is not null)
        {
            // Changing page size must not invalidate a cursor or cap the complete result.
            var page = await repository.GetNodesPage(
                request with
                {
                    Cursor = cursor,
                    PageSize = 5_000,
                    IncludeTotal = false
                },
                ct);
            page.Revision.Should().Be(first.Revision);
            page.TotalCount.Should().BeNull();
            nodes.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        nodes.Count.Should().Be(first.TotalCount);
        (nodes.Count > 1_000).Should().BeTrue();
        nodes
            .Select(node => node.Id)
            .Distinct()
            .Count().Should().Be(nodes.Count);
        nodes.Should().Equal(nodes
            .OrderBy(node => node.Scope == "selected" ? 0 : 1)
            .ThenBy(node => node.Id));

        var identities = await context.GraphNodes.ToDictionaryAsync(node => node.CanonicalId, node => node.Id, ct);
        var selected = graph.CodeNodes
            .Where(node => directory == "" || node.RelativeFilePath.StartsWith(
                directory + "/",
                StringComparison.Ordinal))
            .Select(node => node.CanonicalId)
            .Concat(graph.Projects
                .Where(project => directory == "" || project.RelativeFilePath.StartsWith(
                    directory + "/",
                    StringComparison.Ordinal))
                .Select(project => project.Id))
            .ToHashSet();
        var expectedEdges = graph.Edges
            .Where(edge => selected.Contains(edge.CallerId) || selected.Contains(edge.CalleeId))
            .ToArray();
        var expectedNodes = selected.Concat(expectedEdges
            .SelectMany(edge => new[] { edge.CallerId, edge.CalleeId }))
            .ToHashSet();
        nodes
            .Select(node => node.Id)
            .Order().Should().Equal(expectedNodes
                .Select(id => identities[id])
                .Order());
        foreach (var canonicalId in expectedNodes)
        {
            var node = nodes.Should().ContainSingle(node => node.Id == identities[canonicalId]).Which;
            var code = graph.CodeNodes.SingleOrDefault(candidate => candidate.CanonicalId == canonicalId);
            node.Label.Should().Be(code?.FullyQualifiedName ?? graph.Projects.Single(project => project.Id == canonicalId).Name);
            node.Scope.Should().Be(selected.Contains(canonicalId) ? "selected" : "external");
            node.CodeNodeId.Should().Be(code is null ? (int?)null : identities[canonicalId]);
        }

        var edgeRequest = request with
        {
            Revision = first.Revision,
            PageSize = 337
        };
        var firstEdges = await repository.GetEdgesPage(edgeRequest, ct);
        var edges = new List<GraphPageEdge>(firstEdges.Items);
        cursor = firstEdges.NextCursor;
        while (cursor is not null)
        {
            var page = await repository.GetEdgesPage(
                edgeRequest with
                {
                    Cursor = cursor,
                    PageSize = 701,
                    IncludeTotal = false
                },
                ct);
            edges.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        edges.Count.Should().Be(firstEdges.TotalCount);
        (edges.Count > 2_000).Should().BeTrue();
        edges
            .Select(edge => (edge.Source, edge.Target, edge.Type))
            .Distinct()
            .Count().Should().Be(edges.Count);
        edges
            .Select(edge => (edge.Source, edge.Target, edge.Type))
            .Order().Should().Equal(expectedEdges
                .Select(edge => (identities[edge.CallerId], identities[edge.CalleeId], edge.EdgeType.ToString()
                    .ToLowerInvariant()))
                .Order());
        var nodeIds = nodes
            .Select(node => node.Id)
            .ToHashSet();
        edges.Should().AllSatisfy(edge => (nodeIds.Contains(edge.Source) && nodeIds.Contains(edge.Target)).Should().BeTrue());
    }

    [Fact]
    public async Task WhenSelectedPageEndsExactlyAtBoundary_ThenLoadsHttpAndPackageNodesOnFollowingPages()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        var graph = Snapshot(10);
        graph = graph with
        {
            Edges =
            [
                .. graph.Edges,
                new("code:0", "http:GET:%2Fapi%2Fusers", EdgeType.HttpRequest, "/api/users"),
                new("code:0", "package:%40mui%2Fmaterial:Box", EdgeType.Import)
            ]
        };
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory()).ReplaceWorkspace(graph, ct);
        await using var context = await factory.GetContext(ct);
        var root = await context.Directories
            .Where(item => item.Path == "")
            .Select(item => item.Id)
            .SingleAsync(ct);
        var repository = new GraphPageRepository(context, Workspace());
        var selected = await repository.GetNodesPage(new([root], PageSize: 12, IncludeTotal: true), ct);
        selected.TotalCount.Should().Be(14);
        selected.Items.Should().AllSatisfy(node => node.Scope.Should().Be("selected"));

        var boundary = await repository.GetNodesPage(new([root], PageSize: 12, Cursor: selected.NextCursor), ct);

        boundary.NextCursor.Should().BeNull();
        boundary.Items.Count.Should().Be(2);
        boundary.Items.Should().AllSatisfy(node => node.Scope.Should().Be("external"));
        boundary.Items.Should().Contain(node => node.Label == "GET /api/users" && node.Type == "http");
        boundary.Items.Should().Contain(node => node.Label == "@mui/material/Box" && node.Type == "package");
        var edges = await repository.GetEdgesPage(new([root]), ct);
        edges.Items.Should().Contain(edge => edge.Type == "http-request" && edge.Scope == "boundary" && edge.Metadata == "/api/users");
    }

    [Fact]
    public async Task WhenGraphChanges_ThenRejectsOldCursorsButUnchangedReindexPreservesRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        var writer = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var graph = Snapshot(10);
        await writer.ReplaceWorkspace(graph, ct);
        await using var context = await factory.GetContext(ct);
        var root = await context.Directories
            .Where(item => item.Path == "")
            .Select(item => item.Id)
            .SingleAsync(ct);
        var repository = new GraphPageRepository(context, Workspace());
        var request = new GraphPageRequest([root], PageSize: 2);
        var first = await repository.GetNodesPage(request, ct);

        await writer.ReplaceWorkspace(graph, ct);
        var unchanged = await repository.GetNodesPage(
            request with
            {
                Cursor = first.NextCursor
            },
            ct);
        unchanged.Revision.Should().Be(first.Revision);

        var changedNode = graph.CodeNodes[0] with
        {
            Summary = "Changed"
        };
        await writer.ReplaceWorkspace(
            graph with
            {
                CodeNodes = graph.CodeNodes
                    .Select(node => node.CanonicalId == changedNode.CanonicalId ? changedNode : node)
                    .ToArray()
            },
            ct);

        await ((Func<Task>)(() => repository.GetNodesPage(
            request with
            {
                Cursor = first.NextCursor
            },
            ct))).Should().ThrowExactlyAsync<GraphRevisionChangedException>();
        await ((Func<Task>)(() => repository.GetEdgesPage(
            request with
            {
                Revision = first.Revision
            },
            ct))).Should().ThrowExactlyAsync<GraphRevisionChangedException>();
        (await repository.GetNodesPage(request, ct)).Revision.Should().NotBe(first.Revision);
    }

    [Fact]
    public async Task WhenCursorBelongsToAnotherScopeWorkspaceOrPageType_ThenRejectsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory()).ReplaceWorkspace(Snapshot(10), ct);
        await using var context = await factory.GetContext(ct);
        var root = await context.Directories
            .Where(item => item.Path == "")
            .Select(item => item.Id)
            .SingleAsync(ct);
        var child = await context.Directories
            .Where(item => item.Path == "A")
            .Select(item => item.Id)
            .SingleAsync(ct);
        var repository = new GraphPageRepository(context, Workspace());
        var request = new GraphPageRequest([root], PageSize: 2);
        var first = await repository.GetNodesPage(request, ct);
        var next = request with
        {
            Cursor = first.NextCursor
        };

        await ((Func<Task>)(() => repository.GetNodesPage(
            next with
            {
                DirectoryIds = [child]
            },
            ct))).Should().ThrowExactlyAsync<ArgumentException>();
        await ((Func<Task>)(() => repository.GetEdgesPage(next, ct))).Should().ThrowExactlyAsync<ArgumentException>();
        await ((Func<Task>)(() => repository.GetNodesPage(
            next with
            {
                Cursor = "broken!"
            },
            ct))).Should().ThrowExactlyAsync<ArgumentException>();
        await ((Func<Task>)(() => new GraphPageRepository(
            context,
            Workspace("other")).GetNodesPage(next, ct))).Should().ThrowExactlyAsync<ArgumentException>();
        await ((Func<Task>)(() => repository.GetNodesPage(
            request with
            {
                PageSize = 5_001
            },
            ct))).Should().ThrowExactlyAsync<ArgumentException>();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancelled.Cancel();
        await ((Func<Task>)(() => repository.GetNodesPage(request, cancelled.Token))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenRevisionChangesDuringPageRead_ThenDiscardsMixedPage(bool connections)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new(UseMigrations: true, LoadVectorExtension: true));
        await new KnowledgeGraphRepository(factory.CreateDbContextFactory()).ReplaceWorkspace(Snapshot(10), ct);
        var options = new DbContextOptionsBuilder<SharpSenseDbContext>().UseSqlite(factory.GetSqliteConnection())
            .AddInterceptors(new ChangeRevisionAfterRead()).Options;
        await using var context = new SharpSenseDbContext(options);
        var root = await context.Directories
            .Where(item => item.Path == "")
            .Select(item => item.Id)
            .SingleAsync(ct);
        var nodeId = await context.CodeNodes
            .Select(node => node.Id)
            .FirstAsync(ct);

        await ((Func<Task>)(async () =>
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
        })).Should().ThrowExactlyAsync<GraphRevisionChangedException>();
    }

    private sealed class ChangeRevisionAfterRead : DbCommandInterceptor
    {
        private bool _changed;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
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
        => new(
            "/repo",
            $"/workspace-home/{name}/index.db",
            new FileSystem());

    private static ExtractedNodes Snapshot(int count)
    {
        var nodes = Enumerable.Range(0, count)
            .Select(index =>
            {
                var project = index < count / 2 ? "A" : "B";

                return new IndexedCodeNode(
                    $"code:{index}",
                    $"project:{project}",
                    $"Fixture.Node{index}",
                    $"Node{index}",
                    NodeType.Class,
                    $"{project}/Node.cs",
                    1,
                    3,
                    "Node summary",
                    $"Node{index}",
                    "original");
            })
            .ToArray();
        var edges = Enumerable.Range(0, count)
            .SelectMany(index => new[]
        {
            new IndexedDependency(
                nodes[index].CanonicalId,
                nodes[(index + 1) % count].CanonicalId,
                EdgeType.MethodCall),
            new IndexedDependency(nodes[index].CanonicalId, nodes[(index + 1) % count].CanonicalId, EdgeType.Implements)
        })
            .ToArray();

        return new(
            [new("project:A", "A", "A/A.csproj", "A"), new("project:B", "B", "B/B.csproj", "B")],
            nodes,
            [.. edges, new("project:A", "project:B", EdgeType.ProjectReference)],
            []);
    }
}
