using FluentResults;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Memory.AttachMemory;

public sealed class AttachMemoryCommandHandler(IMemoryWriter memoryWriter)
    : ICommandHandler<AttachMemoryCommand, Result>
{
    public Task<Result> Handle(AttachMemoryCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        return memoryWriter.AttachMemory(
            command.NodeId,
            command.Content,
            command.Tags,
            ct);
    }
}
