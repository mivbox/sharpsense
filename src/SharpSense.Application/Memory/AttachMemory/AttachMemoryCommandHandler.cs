using FluentResults;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Memory.AttachMemory;

internal sealed class AttachMemoryCommandHandler(IMemoryRepository memoryRepository)
    : ICommandHandler<AttachMemoryCommand, Result>
{
    public Task<Result> Handle(AttachMemoryCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        return memoryRepository.AttachMemory(
            command.NodeId,
            command.Content,
            command.Tags,
            command.Intent,
            ct);
    }
}
