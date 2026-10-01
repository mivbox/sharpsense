using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing;

internal sealed record WorkspaceExtractionStep(
    WorkspaceSource Source,
    ILanguageExtractor Extractor,
    ExtractionContext Context);
