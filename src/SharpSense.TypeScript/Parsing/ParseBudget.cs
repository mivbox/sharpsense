using System.Runtime.CompilerServices;

namespace SharpSense.TypeScript.Parsing;

internal sealed class ParseBudget(TypeScriptParseOptions options, CancellationToken cancellationToken)
{
    private int _depth;
    private int _nodes;

    internal CancellationToken CancellationToken => cancellationToken;

    internal RecursionScope Enter(int offset)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_depth >= options.MaxRecursionDepth || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw new TypeScriptParseLimitException(nameof(options.MaxRecursionDepth), offset);
        }

        _depth++;

        return new RecursionScope(this);
    }

    internal void CompleteNode(int offset)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++_nodes > options.MaxNodeCount)
        {
            throw new TypeScriptParseLimitException(nameof(options.MaxNodeCount), offset);
        }
    }

    internal readonly struct RecursionScope(ParseBudget budget) : IDisposable
    {
        public void Dispose() => budget._depth--;
    }
}
