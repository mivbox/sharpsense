using FluentResults;

namespace SharpSense.Application.Memory.Abstractions;

/// <summary>
/// Persists semantic memory against the current logical identity of a code node so write callers can attach human or
/// AI intent without taking a dependency on transient code-node ids, hashing details, or embedding reuse rules.
/// </summary>
public interface IMemoryWriter
{
    /// <summary>
    /// Attaches a semantic memory payload to the supplied persisted code-node id after resolving the node to its
    /// current fully-qualified name and body hash.
    /// </summary>
    Task<Result> AttachMemory(
        int nodeId,
        string content,
        string[]? tags,
        CancellationToken ct);
}
