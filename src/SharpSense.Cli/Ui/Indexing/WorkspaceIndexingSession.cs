using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
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
        update(new WorkspaceIndexingUpdate("indexing", "Preparing workspace database..."));
        await services.GetRequiredService<WorkspaceDatabaseInitializer>().InitializeAsync(ct);

        var notifier = new SessionAnalysisNotifier(update);
        var indexer = services.GetRequiredService<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>();
        async Task Initialize(CancellationToken token)
        {
            var result = await indexer.Handle(new IndexTargetCommand(Notifier: notifier), token);
            if (result.IsFailed)
            {
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors.Select(static error => error.Message)));
            }

            update(new WorkspaceIndexingUpdate(
                "indexing", "Workspace index saved."));
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
            var changed = await updater.Handle(new UpdateWorkspaceFilesCommand(changes, Notifier: notifier), token);
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
                Diagnostics: changed.IsSuccess ? null : [.. changed.Errors.Select(static error => error.Message)]));
            token.ThrowIfCancellationRequested();
        }, ct, initialize: Initialize, onReady: () =>
        {
            ready = true;
            update(new WorkspaceIndexingUpdate("watching", "Watching workspace sources for changes."));
        });
    }

    private sealed class SessionAnalysisNotifier(Action<WorkspaceIndexingUpdate> update) : IAnalysisNotifier
    {
        private readonly AnalysisSnapshotStore _store = new();

        public void Notify(AnalysisNotification notification)
        {
            if (_store.Apply(notification) is not { } snapshot)
            {
                return;
            }

            update(new WorkspaceIndexingUpdate(
                "indexing", snapshot.Message ?? "Analyzing workspace...",
                snapshot.CompletedItems, snapshot.TotalItems, snapshot.Diagnostics,
                IndexCommitted: notification.Kind == AnalysisNotificationKind.Committed,
                Analysis: snapshot));
        }
    }
}
