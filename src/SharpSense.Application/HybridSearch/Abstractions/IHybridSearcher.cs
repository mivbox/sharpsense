using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;

namespace SharpSense.Application.HybridSearch.Abstractions;

public interface IHybridSearcher
{
    Task<HybridSearchResult> Search(HybridSearchQuery query, CancellationToken ct);
}
