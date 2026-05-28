using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Memory.GetNodeMemories;

public sealed class GetNodeMemoriesQueryHandler(IMemoryReader memoryReader)
    : IQueryHandler<GetNodeMemoriesQuery, MemoryNode[]>
{
    public async Task<MemoryNode[]> Handle(GetNodeMemoriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.NodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query.NodeId), query.NodeId, "NodeId must be greater than zero.");
        }

        var memoriesByNodeId = await memoryReader.GetNodeMemories([query.NodeId], ct);
        if (!memoriesByNodeId.TryGetValue(query.NodeId, out var memories))
        {
            throw new InvalidOperationException($"No persisted node exists for id {query.NodeId}.");
        }

        return memories;
    }
}
