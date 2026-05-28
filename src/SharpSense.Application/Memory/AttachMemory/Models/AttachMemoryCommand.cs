namespace SharpSense.Application.Memory.AttachMemory.Models;

public sealed record AttachMemoryCommand(
    int NodeId,
    string Content,
    string[]? Tags);
