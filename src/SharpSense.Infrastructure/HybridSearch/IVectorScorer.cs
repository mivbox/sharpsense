using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.HybridSearch;

/// <summary>
/// Computes similarity scores for hydrated keyword candidates against a generated query embedding. This separates the
/// SQLite vector-distance query from <see cref="HybridSearcher"/> so the orchestrator only coordinates filtering,
/// hydration, and final ranking.
/// </summary>
public interface IVectorScorer
{
    /// <summary>
    /// Returns normalized vector scores for the supplied candidate ids using the provided query embedding.
    /// </summary>
    Task<Dictionary<int, float>> GetScoresAsync(SharpSenseDbContext context,
        IReadOnlyList<int> candidateIds,
        float[] queryVector,
        CancellationToken ct);
}

