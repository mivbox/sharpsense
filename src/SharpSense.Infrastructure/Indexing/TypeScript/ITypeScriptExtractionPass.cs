using JetBrains.Annotations;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

/// <summary>
/// Defines one isolated stage in the TypeScript extraction pipeline so each pass can focus on one concern while sharing
/// the same discovered-file and extraction-output context. Implementations are expected to mutate the supplied context
/// only by appending projects, code nodes, dependency edges, or diagnostics for the current TypeScript indexing run.
/// </summary>
[PublicAPI]
public interface ITypeScriptExtractionPass
{
    /// <summary>
    /// Executes the extraction stage against the shared pass context for the current TypeScript indexing run.
    /// </summary>
    /// <param name="context">The mutable shared context for the current TypeScript extraction pipeline.</param>
    void Execute(TypeScriptPassContext context);
}
