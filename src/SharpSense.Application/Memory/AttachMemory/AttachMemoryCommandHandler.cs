using FluentResults;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Memory.AttachMemory;

internal sealed class AttachMemoryCommandHandler(IMemoryRepository memoryRepository)
    : ICommandHandler<AttachMemoryCommand, Result<MemoryNode>>
{
    public Task<Result<MemoryNode>> Handle(AttachMemoryCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Enum.IsDefined(command.Intent))
        {
            return Task.FromResult(Result.Fail<MemoryNode>(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "Memory intent must be one of: Convention, Invariant, Todo, Warning, Decision.")));
        }

        if (command.Tags?.Any(static tag => tag is null) == true)
        {
            return Task.FromResult(Result.Fail<MemoryNode>(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "Memory tags must not contain null values.")));
        }

        return memoryRepository.AttachMemory(
            command.NodeId,
            command.Content,
            command.Tags,
            command.Intent,
            ct);
    }
}
