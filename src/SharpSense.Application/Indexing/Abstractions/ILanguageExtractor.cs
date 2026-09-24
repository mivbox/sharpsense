using FluentResults;
using JetBrains.Annotations;
using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing.Abstractions;

/// <summary>
/// Defines a language-specific extraction strategy so the indexing command handlers can compose extraction behavior through
/// dependency injection instead of hard-coding Roslyn and Markdown collaborators. Implementations translate one source
/// language or document family into the shared project, node, edge, and diagnostic model consumed by the downstream
/// embedding and persistence phases.
/// </summary>
[PublicAPI]
public interface ILanguageExtractor
{
    WorkspaceSourceKind? SourceKind => null;

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
    /// <returns>
    /// <see cref="Result.Ok(T)"/> with the extracted batch, or <see cref="Result.Fail(string)"/> with one or more
    /// <see cref="SharpSense.Application.Shared.Errors.ServiceError"/> entries when the underlying workspace load
    /// or analysis pipeline reports a typed failure.
    /// </returns>
    Task<Result<ExtractedNodes>> Extract(
        ExtractionContext context,
        CancellationToken ct);

    /// <summary>
    /// Extracts only the knowledge-graph delta for the changed workspace files described by the supplied context.
    /// Implementations are responsible for honoring cancellation, reporting progress when appropriate, and returning
    /// only the replacement nodes and dependency edges that should be persisted for the current change set.
    /// </summary>
    /// <param name="context">The incremental indexing context containing the watched target path and file changes.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current indexing operation.</param>
    /// <returns>
    /// <see cref="Result.Ok(T)"/> with the extracted delta, or <see cref="Result.Fail(string)"/> when the underlying
    /// incremental reload reports a typed failure.
    /// </returns>
    Task<Result<ExtractedNodes>> ExtractIncremental(
        IncrementalExtractionContext context,
        CancellationToken ct);
}
