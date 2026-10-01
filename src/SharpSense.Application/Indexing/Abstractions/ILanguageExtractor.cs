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
    WorkspaceSourceKind SourceKind { get; }

    /// <summary>
    /// Extracts all knowledge-graph data handled by this strategy for the absolute target path described by the supplied
    /// context. Implementations are responsible for honoring cancellation, reporting extraction progress through the
    /// shared context when appropriate, and returning only extracted projects, code nodes, dependency edges, and
    /// diagnostics without performing embeddings or persistence.
    /// </summary>
    /// <param name="context">The indexing context containing the absolute target path and shared progress reporter.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current indexing operation.</param>
    /// <returns>
    /// A successful result containing the extracted batch, or a failed result with one or more
    /// <see cref="SharpSense.Application.Shared.Errors.ServiceError"/> entries when the underlying workspace load
    /// or analysis pipeline reports a typed failure.
    /// </returns>
    Task<Result<ExtractedNodes>> Extract(
        ExtractionContext context,
        CancellationToken ct);
}
