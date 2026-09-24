using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Cli.Ui.Indexing;

internal static class WorkspaceIndexingSession
{
    public static async Task Run(
        IServiceScopeFactory scopeFactory,
        WorkspaceCatalog catalog,
        WorkspaceSelection selection,
        StartWorkspaceIndexingRequest request,
        Action<WorkspaceIndexingUpdate> update,
        CancellationToken ct)
    {
        using var lease = catalog.AcquireIndexLease(selection);
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<WorkspaceScope>().Bind(selection, request.Watch, request.SkipEmbeddings);
        await services.GetRequiredService<WorkspaceDatabaseInitializer>().InitializeAsync(ct);

        var progress = new CallbackProgress<IndexingProgress>(value => update(new WorkspaceIndexingUpdate(
            "indexing", value.CurrentTask, value.CompletedItems, value.TotalItems)));
        var embeddingProgress = new CallbackProgress<EmbeddingGenerationProgress>(value => update(new WorkspaceIndexingUpdate(
            "indexing", value.CurrentTask, value.CompletedItems, value.TotalItems)));
        var indexer = services.GetRequiredService<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>();
        async Task Initialize(CancellationToken token)
        {
            var result = await indexer.Handle(new IndexTargetCommand(progress, embeddingProgress), token);
            if (result.IsFailed)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors.Select(static error => error.Message)));
            }

            update(new WorkspaceIndexingUpdate(
                "indexing", "Workspace index saved.", Diagnostics: [], IndexCommitted: true));
            token.ThrowIfCancellationRequested();
        }

        if (!request.Watch)
        {
            await Initialize(ct);
            return;
        }

        var ready = false;
        var watcher = services.GetRequiredService<IWorkspaceWatcher>();
        var updater = services.GetRequiredService<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>>();
        await watcher.Watch(selection.Workspace.RootPath, async (changes, token) =>
        {
            var changed = await updater.Handle(new UpdateWorkspaceFilesCommand(changes, progress), token);
            if (changed.IsFailed && !ready)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException(string.Join(Environment.NewLine, changed.Errors.Select(static error => error.Message)));
            }

            update(new WorkspaceIndexingUpdate(
                ready ? "watching" : "indexing",
                changed.IsSuccess
                    ? ready ? "Watching workspace sources for changes." : "Reconciling changes received during indexing."
                    : "Update failed. Watching for the next change.",
                Diagnostics: changed.IsSuccess ? [] : [.. changed.Errors.Select(static error => error.Message)],
                IndexCommitted: changed.IsSuccess && changed.Value.IndexCommitted));
            token.ThrowIfCancellationRequested();
        }, ct, initialize: Initialize, onReady: () =>
        {
            ready = true;
            update(new WorkspaceIndexingUpdate("watching", "Watching workspace sources for changes.", Diagnostics: []));
        });
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
