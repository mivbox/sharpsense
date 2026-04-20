using SharpSense.Application.Features.HybridSearch.Contracts;
using SharpSense.Application.Features.HybridSearch.Infrastructure;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Features.HybridSearch.HybridSearch;

public sealed class HybridSearchQueryHandler(IHybridSearchService searchService)
    : IQueryHandler<HybridSearchQuery, HybridSearchResult>
{
    public Task<HybridSearchResult> Handle(HybridSearchQuery query, CancellationToken ct)
        => searchService.Search(query, ct);
}
