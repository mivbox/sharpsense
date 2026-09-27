using FluentResults;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Memory.GetMemory;

internal sealed class GetMemoryQueryHandler(IMemoryRepository memoryRepository)
    : IQueryHandler<GetMemoryQuery, Result<MemoryNode>>
{
    public async Task<Result<MemoryNode>> Handle(GetMemoryQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.MemoryId == Guid.Empty)
        {
            return Result.Fail("Memory id must not be empty.");
        }

        var memory = await memoryRepository.GetMemory(query.MemoryId, ct);
        if (memory is null)
        {
            return Result.Fail($"No persisted memory exists for id {query.MemoryId}.");
        }

        return Result.Ok(memory);
    }
}
