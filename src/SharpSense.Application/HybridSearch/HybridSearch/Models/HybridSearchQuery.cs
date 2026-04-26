using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.HybridSearch.HybridSearch.Models;

public sealed record HybridSearchQuery(
    string SearchText,
    int Limit = 10,
    string? ProjectId = null,
    NodeType[]? IncludedNodeTypes = null);
