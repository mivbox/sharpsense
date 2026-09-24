using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;

namespace SharpSense.Cli.Analyze;

/// <summary>One lease and one bound scope cover initialization and the entire analysis/watch session.</summary>
internal sealed class WorkspaceAnalysisRunner(IServiceScopeFactory scopeFactory, WorkspaceCatalog catalog, IAnsiConsole console)
{
    public async Task<int> Run(WorkspaceSelection selection, AnalyzeCommand.Settings settings, CancellationToken ct)
    {
        using var lease = catalog.AcquireIndexLease(selection);
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<WorkspaceScope>().Bind(selection, settings.Watch, settings.SkipEmbeddings, settings.DisableEmbeddingCache);
        var presentation = new SpectreAnalysisNotifier(console, selection.Definition.Name);
        using var stopping = ct.Register(() => presentation.SetState("Stopping analysis..."));
        try
        {
            await presentation.Run(async () =>
            {
                presentation.SetState("Preparing workspace database...");
                await services.GetRequiredService<IAnalysisDatabaseInitializer>().Initialize(ct);
                if (settings.Watch)
                {
                    await Watch(services, selection.Workspace.RootPath, presentation, ct);
                }
                else
                {
                    await Index(services, presentation, ct);
                    presentation.SetState($"Indexed workspace '{selection.Definition.Name}'.");
                }
            });
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            console.WriteLine("Analysis stopped.");
            return 0;
        }
        catch (IndexingFailedException ex)
        {
            console.MarkupLine($"[red]ERROR[/]: {Markup.Escape(ex.Message)}");
            return 1;
        }
    }

    private static async Task Index(IServiceProvider services, SpectreAnalysisNotifier presentation, CancellationToken ct, bool recovering = false)
    {
        presentation.SetState(recovering ? "Recovering: rebuilding the full index..." : "Analyzing workspace...");
        var handler = services.GetRequiredService<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>();
        var result = await handler.Handle(new IndexTargetCommand(Notifier: presentation), ct);
        ct.ThrowIfCancellationRequested();
        if (result.IsFailed)
        {
            throw new IndexingFailedException(string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        }
    }

    private static async Task Watch(IServiceProvider services, string root, SpectreAnalysisNotifier presentation, CancellationToken ct)
    {
        var watcher = services.GetRequiredService<IWorkspaceWatcher>();
        var recovering = false;
        var ready = false;
        while (!ct.IsCancellationRequested)
        {
            var subscribed = false;
            ready = false;
            try
            {
                await watcher.Watch(root, ApplyBatch, ct, initialize: async token =>
                {
                    subscribed = true;
                    if (recovering)
                    {
                        await Recover(token);
                    }
                    else
                    {
                        await Index(services, presentation, token);
                    }
                    presentation.SetState("Reconciling changes received during indexing...");
                }, onReady: () =>
                {
                    ready = true;
                    presentation.SetState("Watching workspace sources for changes. Press Ctrl+C to stop.");
                });
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (IndexingFailedException)
            {
                throw;
            }
            catch (Exception ex) when (subscribed)
            {
                Log.ForContext<WorkspaceAnalysisRunner>().Warning(ex, "Workspace watcher failed.");
                recovering = true;
                presentation.SetState("Restarting watch mode after a file system error...");
            }
        }

        return;

        async Task ApplyBatch(IReadOnlyList<WorkspaceFileChange> changes, CancellationToken token)
        {
            try
            {
                presentation.SetState($"Updating {changes.Count} changed file(s)...");
                var handler = services.GetRequiredService<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>>();
                var result = await handler.Handle(new UpdateWorkspaceFilesCommand(changes, Notifier: presentation), token);
                token.ThrowIfCancellationRequested();
                if (result.IsFailed)
                {
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Message)));
                }
                presentation.SetState(!ready ? "Reconciling changes received during indexing..."
                    : result.Value.IndexCommitted ? "Workspace update saved. Watching for changes."
                    : "No indexed sources changed. Watching for changes.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.ForContext<WorkspaceAnalysisRunner>().Warning(ex, "Incremental watch update failed.");
                await Recover(token);
                presentation.SetState(ready ? "Watching workspace sources for changes. Press Ctrl+C to stop."
                    : "Reconciling changes received during indexing...");
            }
        }

        async Task Recover(CancellationToken token)
        {
            presentation.SetState("Recovering: rebuilding the full index...");
            await Index(services, presentation, token, recovering: true);
            presentation.SetState("Watch mode recovery completed.");
        }
    }

    private sealed class IndexingFailedException(string message) : Exception(message);
}
