using FluentResults;
using SharpSense.Application.HybridSearch.Abstractions;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.HybridSearch.HybridSearch;

internal sealed class HybridSearchQueryHandler(IHybridSearcher searcher)
    : IQueryHandler<HybridSearchQuery, Result<HybridSearchResult>>
{
    public Task<Result<HybridSearchResult>> Handle(
        HybridSearchQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.IncludedNodeTypes?.Any(static nodeType => !Enum.IsDefined(nodeType) || nodeType == NodeType.Memory) == true)
        {
            return Task.FromResult(Result.Fail<HybridSearchResult>(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "NodeTypes must contain valid code or document node types (Class, Interface, Method, Property, Field, Document, Component).")));
        }

        if (query.TagFilters?.Any(static tag => tag is null) == true)
        {
            return Task.FromResult(Result.Fail<HybridSearchResult>(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "Tags must not contain null values.")));
        }

        return searcher.Search(query, ct);
    }
}
