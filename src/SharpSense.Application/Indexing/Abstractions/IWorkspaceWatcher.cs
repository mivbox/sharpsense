using JetBrains.Annotations;
using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing.Abstractions;

/// <summary>
/// Observes a repository workspace for relevant file changes and groups them into debounced batches so higher layers can
/// react without depending on file-system watching primitives. Implementations own the watch infrastructure, filter
/// irrelevant paths, honor cancellation, and invoke the supplied callback for each relevant change batch.
/// </summary>
[PublicAPI]
public interface IWorkspaceWatcher
{
    /// <summary>
    /// Starts watching the supplied repository root and invokes <paramref name="onBatchChanged"/> whenever a debounced
    /// batch of relevant workspace changes is detected. Implementations are responsible for surfacing fatal watcher
    /// failures by throwing so the caller can decide whether to recover, restart, or stop watching.
    /// </summary>
    /// <param name="repositoryRoot">Absolute repository root path whose contents should be observed.</param>
    /// <param name="onBatchChanged">Callback that receives each debounced batch of relevant workspace file changes.</param>
    /// <param name="ct"><see cref="CancellationToken"/> for the current watch session.</param>
    /// <param name="initialize">Optional initial indexing, executed after subscribing while changes are buffered.</param>
    /// <param name="onReady">Called after initialization and all buffered changes have been reconciled.</param>
    Task Watch(
        string repositoryRoot,
        Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> onBatchChanged,
        CancellationToken ct,
        Func<CancellationToken, Task>? initialize = null,
        Action? onReady = null);
}
