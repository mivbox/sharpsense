using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.DependencyGraph;

public sealed partial class GraphPageRepository(SharpSenseDbContext context, IRepositoryWorkspace workspace)
    : IGraphPageRepository
{
    public async Task<GraphNodesPage> GetNodesPage(GraphPageRequest request, CancellationToken ct)
    {
        var directories = Validate(request);
        var key = workspace.WorkspaceId?.ToString("D") ?? workspace.DatabasePath;
        var scope = GraphPageCursor.ScopeHash(directories);
        var cursor = GraphPageCursor.Decode(request.Cursor, request.Revision, key, scope, "nodes");
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            var revision = await ReadRevision(ct);
            CheckRevision(cursor?.Revision ?? request.Revision, revision);
            var nodes = new List<GraphPageNode>();
            GraphPageCursor? next = null;
            var rootSelected = await RootSelected(directories, ct);
            var hasBoundary = !rootSelected || await context.GraphNodes.AsNoTracking()
                .AnyAsync(node => node.Kind == Persistence.Records.GraphNodeKind.Http || node.Kind == Persistence.Records.GraphNodeKind.Package, ct);

            if (directories.Length > 0)
            {
                if (cursor?.Phase is null or 0)
                {
                    var selected = await ReadNodes(directories, false, rootSelected, cursor?.NodeId ?? 0, request.PageSize + 1, ct);
                    nodes.AddRange(selected.Take(request.PageSize));
                    if (selected.Count > request.PageSize)
                    {
                        next = new(key, scope, revision, "nodes", NodeId: nodes[^1].Id);
                    }
                }

                if (next is null && hasBoundary)
                {
                    var remaining = request.PageSize - nodes.Count;
                    var external = await ReadNodes(directories, true, rootSelected,
                        cursor?.Phase == 1 ? cursor.NodeId : 0, remaining + 1, ct);
                    nodes.AddRange(external.Take(remaining));
                    if (external.Count > remaining)
                    {
                        next = new(key, scope, revision, "nodes", Phase: 1,
                            NodeId: remaining == 0 ? 0 : nodes[^1].Id);
                    }
                }
            }

            int? total = request.IncludeTotal ? await CountNodes(directories, rootSelected, hasBoundary, ct) : null;
            CheckRevision(revision, await ReadRevision(ct));
            return new(revision, nodes, next?.Encode(), total);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    public async Task<GraphEdgesPage> GetEdgesPage(GraphPageRequest request, CancellationToken ct)
    {
        var directories = Validate(request);
        var key = workspace.WorkspaceId?.ToString("D") ?? workspace.DatabasePath;
        var scope = GraphPageCursor.ScopeHash(directories);
        var cursor = GraphPageCursor.Decode(request.Cursor, request.Revision, key, scope, "edges");
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            var revision = await ReadRevision(ct);
            CheckRevision(cursor?.Revision ?? request.Revision, revision);
            var edges = new List<GraphPageEdge>();
            var storedTypes = new List<string>();
            var visible = $"({GraphPageSql.SelectedCaller} OR {GraphPageSql.SelectedCallee}) AND {GraphPageSql.ValidEdges}";
            var rootSelected = await RootSelected(directories, ct);
            var scopeSql = GraphPageSql.Scope;
            if (!rootSelected)
            {
                // Seek through document and edge indexes for narrow scopes; scanning the
                // complete graph to return a small folder defeats incremental loading.
                scopeSql += GraphPageSql.SelectedNodes + GraphPageSql.VisibleEdges;
                visible += " AND (e.CallerNodeId, e.CalleeNodeId, e.EdgeType) IN (SELECT * FROM VisibleEdges)";
            }
            if (directories.Length > 0)
            {
                // SQLite's composite primary key supports seeking directly to the next edge.
                await using var command = Command($"""
                    {scopeSql}
                    SELECT e.CallerNodeId, e.CalleeNodeId, e.EdgeType, e.Metadata,
                        CASE WHEN {GraphPageSql.SelectedCaller} AND {GraphPageSql.SelectedCallee} THEN 1 ELSE 0 END
                    {GraphPageSql.EdgeJoins}
                    WHERE (e.CallerNodeId, e.CalleeNodeId, e.EdgeType) > ($caller, $callee, $type)
                        AND {visible}
                    ORDER BY e.CallerNodeId, e.CalleeNodeId, e.EdgeType LIMIT $limit;
                    """, ("$directories", JsonSerializer.Serialize(directories)),
                    ("$caller", cursor?.CallerId ?? 0), ("$callee", cursor?.CalleeId ?? 0),
                    ("$type", cursor?.EdgeType ?? ""), ("$limit", request.PageSize + 1));
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var type = reader.GetString(2);
                    storedTypes.Add(type);
                    edges.Add(new(reader.GetInt32(0), reader.GetInt32(1),
                        GraphProjection.EdgeType(type),
                        reader.GetInt32(4) == 1 ? "internal" : "boundary",
                        reader.IsDBNull(3) ? null : reader.GetString(3)));
                }
            }

            string? next = null;
            if (edges.Count > request.PageSize)
            {
                edges.RemoveAt(edges.Count - 1);
                var last = edges[^1];
                next = new GraphPageCursor(key, scope, revision, "edges", CallerId: last.Source,
                    CalleeId: last.Target, EdgeType: storedTypes[edges.Count - 1]).Encode();
            }

            int? total = request.IncludeTotal
                ? directories.Length == 0 ? 0 : await Count($"{scopeSql} SELECT COUNT(*) {GraphPageSql.EdgeJoins} WHERE {visible}", directories, ct)
                : null;
            CheckRevision(revision, await ReadRevision(ct));
            return new(revision, edges, next, total);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task<List<GraphPageNode>> ReadNodes(int[] directories, bool boundary, bool rootSelected,
        int afterId, int limit, CancellationToken ct)
    {
        var sql = NodeQuery(boundary, rootSelected, count: false);
        await using var command = Command(sql, ("$directories", JsonSerializer.Serialize(directories)),
            ("$after", afterId), ("$limit", limit));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<GraphPageNode>();
        while (await reader.ReadAsync(ct))
        {
            var node = GraphProjection.ReadNode(reader);
            result.Add(new(node.Id, node.Label, node.Type, node.RelativePath, node.ProjectId,
                boundary ? "external" : "selected", !boundary, node.CodeNodeId));
        }
        return result;
    }

    private async Task<int> CountNodes(int[] directories, bool rootSelected, bool hasBoundary, CancellationToken ct)
    {
        if (directories.Length == 0)
        {
            return 0;
        }
        var count = await Count(NodeQuery(false, rootSelected, count: true), directories, ct);
        return hasBoundary ? count + await Count(NodeQuery(true, rootSelected, count: true), directories, ct) : count;
    }

    private static string NodeQuery(bool boundary, bool rootSelected, bool count)
    {
        var withBoundary = boundary && !rootSelected;
        var filter = boundary
            ? rootSelected
                ? """
                  g.Kind IN ('Http', 'Package') AND (
                    EXISTS (SELECT 1 FROM DependencyEdges e WHERE e.CalleeNodeId = g.Id AND
                        (e.CallerNodeId IN (SELECT Id FROM CodeNodes) OR e.CallerNodeId IN (SELECT Id FROM ProjectNodes)))
                    OR EXISTS (SELECT 1 FROM DependencyEdges e WHERE e.CallerNodeId = g.Id AND
                        (e.CalleeNodeId IN (SELECT Id FROM CodeNodes) OR e.CalleeNodeId IN (SELECT Id FROM ProjectNodes)))
                  )
                  """
                : "g.Id IN (SELECT Id FROM BoundaryNodes)"
            : rootSelected ? GraphPageSql.SelectedNode : "g.Id IN (SELECT Id FROM SelectedNodes)";
        return $"""
            {GraphPageSql.Scope}{(!rootSelected ? GraphPageSql.SelectedNodes : "")}{(withBoundary ? GraphPageSql.BoundaryNodes : "")}
            SELECT {(count ? "COUNT(*)" : GraphPageSql.NodeColumns)} {GraphPageSql.NodeJoins}
            WHERE {GraphPageSql.ValidNode} AND {filter}
            {(count ? "" : "AND g.Id > $after ORDER BY g.Id LIMIT $limit")};
            """;
    }

    private async Task<bool> RootSelected(int[] directories, CancellationToken ct)
        => await context.Directories.AsNoTracking()
            .AnyAsync(directory => directory.Path == "" && EF.Parameter(directories).Contains(directory.Id), ct);

    private async Task<int> Count(string sql, int[] directories, CancellationToken ct)
    {
        await using var command = Command(sql, ("$directories", JsonSerializer.Serialize(directories)));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private async Task<string> ReadRevision(CancellationToken ct)
        => await context.IndexRunState.AsNoTracking().Select(state => state.GraphRevision).SingleOrDefaultAsync(ct) ?? "initial";

    private static void CheckRevision(string? expected, string actual)
    {
        if (expected is not null && expected != actual)
        {
            throw new GraphRevisionChangedException();
        }
    }

    private DbCommand Command(string sql, params (string Name, object Value)[] parameters)
    {
        var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }
        return command;
    }

    private static int[] Validate(GraphPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.DirectoryIds);
        if (request.PageSize is < 1 or > 5_000 || request.DirectoryIds.Any(id => id <= 0))
        {
            throw new ArgumentException("Page size must be between 1 and 5000; directory IDs must be positive.", nameof(request));
        }
        if (request.Revision is { Length: 0 or > 64 })
        {
            throw new ArgumentException("Graph revision is invalid.", nameof(request));
        }
        return request.DirectoryIds.Distinct().Order().ToArray();
    }
}
