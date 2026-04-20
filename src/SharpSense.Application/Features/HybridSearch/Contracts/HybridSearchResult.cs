namespace SharpSense.Application.Features.HybridSearch.Contracts;

public sealed record HybridSearchResult(
    string SearchText,
    HybridSearchHit[] Hits);
