using FluentResults;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Memory.DeleteMemory;

public sealed class DeleteMemoryCommandHandler(IMemoryRepository memoryRepository)
    : ICommandHandler<DeleteMemoryCommand, Result>
{
    public Task<Result> Handle(DeleteMemoryCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        return memoryRepository.DeleteMemory(command.MemoryId, ct);
    }
}
