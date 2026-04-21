using SharpSense.Application.Features.HybridSearch.Contracts;
using SharpSense.Application.Features.HybridSearch.HybridSearch;

namespace SharpSense.Application.Features.HybridSearch.Infrastructure;

public interface IHybridSearcher
{
    Task<HybridSearchResult> Search(HybridSearchQuery query, CancellationToken ct);
}
