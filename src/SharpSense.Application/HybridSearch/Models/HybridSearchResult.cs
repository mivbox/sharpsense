namespace SharpSense.Application.HybridSearch.Models;

public sealed record HybridSearchResult(
    string SearchText,
    HybridSearchHit[] Hits);
