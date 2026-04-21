using JetBrains.Annotations;

namespace SharpSense.Application.Features.Indexing.Contracts;

/// <summary>
/// Defines a language-specific extraction strategy so <c>KnowledgeGraphIndexing</c> can compose indexing behavior through
/// dependency injection instead of hard-coding Roslyn and Markdown collaborators. Implementations translate one source
/// language or document family into the shared project, node, edge, and diagnostic model consumed by the downstream
/// embedding and persistence phases.
/// </summary>
[PublicAPI]
public interface ILanguageExtractor
{
    /// <summary>
    /// Gets the stable extractor identifier used by the indexing orchestrator to attribute tracing, progress, and
    /// diagnostics to the extractor that produced a given batch of results.
    /// </summary>
    string ExtractorName { get; }

    /// <summary>
    /// Extracts all knowledge-graph data handled by this strategy for the absolute target path described by the supplied
    /// context. Implementations are responsible for honoring cancellation, reporting extraction progress through the
    /// shared context when appropriate, and returning only extracted projects, code nodes, dependency edges, and
    /// diagnostics without performing embeddings or persistence.
    /// </summary>
    /// <param name="context">The indexing context containing the absolute target path and shared progress reporter.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current indexing operation.</param>
    /// <returns>The extracted projects, code nodes, dependency edges, and diagnostics for this extractor.</returns>
    Task<ExtractedNodes> Extract(
        ExtractionContext context,
        CancellationToken ct);
}
