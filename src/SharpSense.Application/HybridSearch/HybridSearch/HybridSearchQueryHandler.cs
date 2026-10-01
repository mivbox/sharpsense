using FluentResults;
using SharpSense.Application.HybridSearch.Abstractions;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.HybridSearch.HybridSearch;

internal sealed class HybridSearchQueryHandler(IHybridSearcher searcher)
    : IQueryHandler<HybridSearchQuery, Result<HybridSearchResult>>
{
    public Task<Result<HybridSearchResult>> Handle(
        HybridSearchQuery query,
        CancellationToken ct)
        => searcher.Search(query, ct);
}
