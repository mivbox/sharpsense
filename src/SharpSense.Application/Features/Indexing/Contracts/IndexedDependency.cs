using JetBrains.Annotations;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Features.Indexing.Contracts;

[PublicAPI]
public sealed record IndexedDependency(
    string CallerId,
    string CalleeId,
    EdgeType EdgeType);
