using FluentResults;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Memory.GetNodeMemories;

internal sealed class GetNodeMemoriesQueryHandler(IMemoryRepository memoryRepository)
    : IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>
{
    public async Task<Result<MemoryNode[]>> Handle(GetNodeMemoriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.NodeId <= 0)
        {
            return Result.Fail("NodeId must be greater than zero.");
        }

        var memoriesByNodeId = await memoryRepository.GetNodeMemories([query.NodeId], query.IntentFilter, ct);
        if (!memoriesByNodeId.TryGetValue(query.NodeId, out var memories))
        {
            return Result.Fail($"No persisted node exists for id {query.NodeId}.");
        }

        return Result.Ok(memories);
    }
}
