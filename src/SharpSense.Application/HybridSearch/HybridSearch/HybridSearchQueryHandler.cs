using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Abstractions;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.HybridSearch.HybridSearch;

public sealed class HybridSearchQueryHandler(IHybridSearcher searcher)
    : IQueryHandler<HybridSearchQuery, HybridSearchResult>
{
    public Task<HybridSearchResult> Handle(HybridSearchQuery query, CancellationToken ct)
        => searcher.Search(query, ct);
}
