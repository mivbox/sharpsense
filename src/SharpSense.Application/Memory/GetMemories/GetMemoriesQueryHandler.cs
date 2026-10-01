using FluentResults;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Memory.GetMemories;

internal sealed class GetMemoriesQueryHandler(IMemoryRepository memoryRepository)
    : IQueryHandler<GetMemoriesQuery, Result<IReadOnlyDictionary<Guid, MemoryNode>>>
{
    public async Task<Result<IReadOnlyDictionary<Guid, MemoryNode>>> Handle(
        GetMemoriesQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.MemoryIds is null || query.MemoryIds.Length == 0)
        {
            return Result.Fail("At least one memory id is required.");
        }

        var distinctIds = query.MemoryIds
            .Distinct()
            .ToArray();
        var memories = await memoryRepository.GetMemories(distinctIds, ct);

        // GetMemories returns a dictionary keyed by every supplied id with nulls for missing; rebuild into
        // a value-typed dictionary (MemoryNode, not MemoryNode?) so the Result type stays clean.
        var result = new Dictionary<Guid, MemoryNode>(distinctIds.Length);
        foreach (var id in distinctIds)
        {
            if (memories.TryGetValue(id, out var memory) && memory is not null)
            {
                result[id] = memory;
            }
        }

        return Result.Ok<IReadOnlyDictionary<Guid, MemoryNode>>(result);
    }
}
