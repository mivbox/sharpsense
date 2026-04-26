using JetBrains.Annotations;

namespace SharpSense.Application.Features.Indexing.Contracts;

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
    Task Watch(
        string repositoryRoot,
        Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> onBatchChanged,
        CancellationToken ct);
}
