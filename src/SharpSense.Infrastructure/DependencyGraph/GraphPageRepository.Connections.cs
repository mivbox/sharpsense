using Microsoft.EntityFrameworkCore;
using SharpSense.Application.DependencyGraph.Models;
using System.Text.Json;

namespace SharpSense.Infrastructure.DependencyGraph;

internal sealed partial class GraphPageRepository
{
    private const string ConnectedPeers = """
        WITH ConnectedPeers AS (
            SELECT CalleeNodeId AS Id FROM DependencyEdges WHERE CallerNodeId = $node
            UNION
            SELECT CallerNodeId AS Id FROM DependencyEdges WHERE CalleeNodeId = $node
        )
        """;

    public async Task<GraphNodeConnectionsPage> GetNodeConnections(
        GraphNodeConnectionsRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.NodeId <= 0 || request.PageSize is < 1 or > 500)
        {
            throw new ArgumentException(
                "Node ID must be positive; connections page size must be between 1 and 500.",
                nameof(request));
        }
        if (request.Revision is { Length: 0 or > 64 })
        {
            throw new ArgumentException("Graph revision is invalid.", nameof(request));
        }

        var key = workspace.WorkspaceId?.ToString("D") ?? workspace.DatabasePath;
        var scope = GraphPageCursor.ScopeHash([request.NodeId]);
        var cursor = GraphPageCursor.Decode(request.Cursor, request.Revision, key, scope, "connections");
        await context.Database.OpenConnectionAsync(ct);
        try
        {
            var revision = await ReadRevision(ct);
            CheckRevision(cursor?.Revision ?? request.Revision, revision);
            var node = await ReadConnectionNode(request.NodeId, ct);
            if (node is null)
            {
                CheckRevision(revision, await ReadRevision(ct));
                throw new KeyNotFoundException($"No indexed graph node exists for id {request.NodeId}.");
            }

            var peers = await ReadConnectedPeers(request.NodeId, cursor?.NodeId ?? 0, request.PageSize + 1, ct);
            string? next = null;
            if (peers.Count > request.PageSize)
            {
                peers.RemoveAt(peers.Count - 1);
                next = new GraphPageCursor(key, scope, revision, "connections", NodeId: peers[^1].Id).Encode();
            }

            var relationships = await ReadRelationships(request.NodeId, peers, ct);
            int? total = null;
            if (request.IncludeTotal)
            {
                await using var count = Command(
                    $"""
                    {ConnectedPeers}
                    SELECT COUNT(*) {GraphPageSql.NodeJoins}
                    WHERE g.Id IN (SELECT Id FROM ConnectedPeers) AND {GraphPageSql.ValidNode};
                    """,
                    ("$node", request.NodeId));
                total = Convert.ToInt32(await count.ExecuteScalarAsync(ct));
            }

            CheckRevision(revision, await ReadRevision(ct));

            return new GraphNodeConnectionsPage(
                node,
                revision,
                [.. peers.Select(peer => new GraphNodeConnection(peer, relationships[peer.Id]))],
                next,
                total);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private async Task<GraphConnectionNode?> ReadConnectionNode(int nodeId, CancellationToken ct)
    {
        await using var command = Command(
            $"""
            SELECT {GraphPageSql.NodeColumns} {GraphPageSql.NodeJoins}
            WHERE g.Id = $node AND {GraphPageSql.ValidNode};
            """,
            ("$node", nodeId));
        await using var reader = await command.ExecuteReaderAsync(ct);

        return await reader.ReadAsync(ct) ? GraphProjection.ReadNode(reader) : null;
    }

    private async Task<List<GraphConnectionNode>> ReadConnectedPeers(
        int nodeId,
        int afterId,
        int limit,
        CancellationToken ct)
    {
        // Caller/callee indexes restrict discovery to this node's adjacency. UNION makes
        // incoming, outgoing and self relationships share one peer entry across pages.
        await using var command = Command(
            $"""
            {ConnectedPeers}
            SELECT {GraphPageSql.NodeColumns} {GraphPageSql.NodeJoins}
            WHERE g.Id IN (SELECT Id FROM ConnectedPeers) AND g.Id > $after AND {GraphPageSql.ValidNode}
            ORDER BY g.Id LIMIT $limit;
            """,
            ("$node", nodeId),
            ("$after", afterId),
            ("$limit", limit));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var peers = new List<GraphConnectionNode>();
        while (await reader.ReadAsync(ct))
        {
            peers.Add(GraphProjection.ReadNode(reader));
        }

        return peers;
    }

    private async Task<Dictionary<int, List<GraphNodeRelationship>>> ReadRelationships(
        int nodeId,
        IReadOnlyList<GraphConnectionNode> peers,
        CancellationToken ct)
    {
        var relationships = peers.ToDictionary(peer => peer.Id, _ => new List<GraphNodeRelationship>());
        if (peers.Count == 0)
        {
            return relationships;
        }

        // Each selected peer gets every edge type in both directions. The self edge is
        // emitted by the first arm only, and JSON binds the peer IDs as one parameter.
        await using var command = Command(
            """
            SELECT CalleeNodeId AS PeerId, EdgeType, Metadata,
                CASE WHEN CalleeNodeId = $node THEN 'self' ELSE 'outgoing' END AS Direction
            FROM DependencyEdges
            WHERE CallerNodeId = $node AND CalleeNodeId IN (SELECT value FROM json_each($peers))
            UNION ALL
            SELECT CallerNodeId AS PeerId, EdgeType, Metadata, 'incoming' AS Direction
            FROM DependencyEdges
            WHERE CalleeNodeId = $node AND CallerNodeId != $node
                AND CallerNodeId IN (SELECT value FROM json_each($peers))
            ORDER BY PeerId, Direction, EdgeType;
            """,
            ("$node", nodeId),
            ("$peers", JsonSerializer.Serialize(peers.Select(peer => peer.Id))));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            relationships[reader.GetInt32(0)].Add(new GraphNodeRelationship(
                GraphProjection.EdgeType(reader.GetString(1)),
                reader.GetString(3),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return relationships;
    }
}
