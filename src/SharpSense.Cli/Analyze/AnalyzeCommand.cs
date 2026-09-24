using FluentResults;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Serilog;
using SharpSense.Application.Shared.Errors;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Cli.Analyze;

[UsedImplicitly]
internal sealed class AnalyzeCommand : AbstractAsyncCommand<AnalyzeCommand.Settings>
{
    private static ILogger Logger => Log.ForContext<AnalyzeCommand>();

    [UsedImplicitly]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public sealed class Settings : GlobalSettings
    {

        [CommandOption("--watch")] public bool Watch { get; init; }

        [CommandOption("--no-embeddings")] public bool SkipEmbeddings { get; init; }

        [CommandOption("--no-cache")] public bool DisableEmbeddingCache { get; init; }

    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.Configure<SharpSenseCliOptions>(options =>
        {
            options.Watch = settings.Watch;
            options.SkipEmbeddings = settings.SkipEmbeddings;
            options.DisableEmbeddingCache = settings.DisableEmbeddingCache;
        });
        services.AddIndexing();
        services.AddEmbeddingsInfrastructure();
        services.AddIndexingInfrastructure();
        services.AddHostedService<WorkspaceIndexLeaseHostedService>();
        services.AddPersistence();
        services.AddIndexRunRecording();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var workspaceScope = host.Services.CreateAsyncScope();
        var services = workspaceScope.ServiceProvider;
        try
        {
            if (!settings.Watch)
            {
                await IndexTarget(settings, services, ct);
                var workspace = services.GetRequiredService<WorkspaceSelection>();
                CommandOutput.Write(context, $"Indexed workspace '{workspace.Definition.Name}'.{Environment.NewLine}");
                return 0;
            }

            var watchPath = GetWatchPath(services);
            await WatchWorkspace(settings, services, watchPath, ct);
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }
        catch (IndexingFailedException)
        {
            return 1;
        }
    }

    private static double CalculateIndexingProgressPercentage(IndexingProgress progress)
    {
        if (progress.TotalItems <= 0)
        {
            return 0;
        }

        var percentage = Math.Clamp(
            progress.CompletedItems * 100d / progress.TotalItems,
            0,
            100);

        return percentage >= 100 ? 99 : percentage;
    }

    private static double CalculateEmbeddingsProgressPercentage(EmbeddingGenerationProgress progress)
    {
        if (progress.TotalItems <= 0)
        {
            return 0;
        }

        return Math.Clamp(
            progress.CompletedItems * 100d / progress.TotalItems,
            0,
            100);
    }

    private static async Task IndexTarget(
        Settings settings,
        IServiceProvider services,
        CancellationToken ct)
    {
        await AnsiConsole.Console.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async progressContext =>
            {
                var indexingTask = progressContext.AddTask("Loading target...");
                indexingTask.MaxValue = 100;
                var embeddingsTask = progressContext.AddTask("Waiting for embeddings...");
                embeddingsTask.MaxValue = 100;
                var embeddingsUpdated = 0;

                var progress = new Progress<IndexingProgress>(update =>
                {
                    indexingTask.Description = Markup.Escape(update.CurrentTask);
                    indexingTask.Value = CalculateIndexingProgressPercentage(update);
                });

                var embeddingProgress = new Progress<EmbeddingGenerationProgress>(update =>
                {
                    Interlocked.Exchange(ref embeddingsUpdated, 1);
                    embeddingsTask.Description = Markup.Escape(update.CurrentTask);
                    embeddingsTask.Value = CalculateEmbeddingsProgressPercentage(update);
                });

                await RunIndex(settings, services, progress, embeddingProgress, ct);

                indexingTask.Description = "Indexing complete.";
                indexingTask.Value = 100;

                if (Volatile.Read(ref embeddingsUpdated) == 0)
                {
                    embeddingsTask.Description = "No embeddings generated.";
                }

                embeddingsTask.Value = 100;
            });
    }

    private static async Task RunIndex(
        Settings settings,
        IServiceProvider services,
        IProgress<IndexingProgress>? progress,
        IProgress<EmbeddingGenerationProgress>? embeddingProgress,
        CancellationToken ct)
    {
        var handler = services.GetRequiredService<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>();

        var result = await handler.Handle(
            new IndexTargetCommand(
                progress,
                embeddingProgress),
            ct);

        ct.ThrowIfCancellationRequested();
        if (result.IsFailed)
        {
            foreach (var error in result.Errors)
            {
                AnsiConsole.MarkupLine($"[red]ERROR[/]: {Markup.Escape(error.Message)}");
            }
            throw new IndexingFailedException();
        }
    }

    private static async Task WatchWorkspace(
        Settings settings,
        IServiceProvider services,
        string repositoryRoot,
        CancellationToken ct)
    {
        var watcher = services.GetRequiredService<IWorkspaceWatcher>();
        var recovering = false;

        while (!ct.IsCancellationRequested)
        {
            var subscribed = false;
            try
            {
                await watcher.Watch(repositoryRoot, ApplyBatchUpdate, ct,
                    initialize: token =>
                    {
                        subscribed = true;
                        return recovering
                            ? RecoverWithFullReindex(settings, services,
                                "Watch mode detected file system watcher errors. Rebuilding the full index...", token)
                            : IndexTarget(settings, services, token);
                    },
                    onReady: () => AnsiConsole.WriteLine(
                        $"Watching {repositoryRoot} for C#, TypeScript, and Markdown changes..."));
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
                Logger.Warning(ex, "Workspace watcher failed.");
                recovering = true;
                AnsiConsole.WriteLine("Restarting watch mode...");
            }
        }

        return;

        async Task ApplyBatchUpdate(
            IReadOnlyList<WorkspaceFileChange> changedFiles,
            CancellationToken token)
        {
            try
            {
                await ApplyIncrementalUpdate(settings, services, changedFiles, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Incremental watch update failed.");
                await RecoverWithFullReindex(
                    settings,
                    services,
                    "Watch mode incremental update failed. Rebuilding the full index...",
                    token);
            }
        }
    }

    private static async Task ApplyIncrementalUpdate(
        Settings settings,
        IServiceProvider services,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct)
    {
        var handler = services.GetRequiredService<ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>>();

        await AnsiConsole.Progress()
            .AutoClear(true)
            .HideCompleted(true)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async progressContext =>
            {
                var updateTask = progressContext.AddTask($"[green]Indexing {changedFiles.Count} changed file(s)...[/]");
                updateTask.MaxValue = 100;

                var progress = new Progress<IndexingProgress>(update =>
                {
                    updateTask.Description = Markup.Escape(update.CurrentTask);
                    if (update.TotalItems > 0)
                    {
                        updateTask.Value = Math.Clamp(update.CompletedItems * 100d / update.TotalItems, 0, 100);
                    }
                });

                var result = await handler.Handle(
                    new UpdateWorkspaceFilesCommand(changedFiles, progress),
                    ct);

                ct.ThrowIfCancellationRequested();
                if (result.IsFailed)
                {
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(error => error.Message)));
                }

                updateTask.Value = 100;
            });

        var time = DateTime.Now.ToString("HH:mm:ss");
        AnsiConsole.MarkupLine($"[grey][[{time}]][/] [green]INDEXED[/] {changedFiles.Count} file(s).");
    }

    private static async Task RecoverWithFullReindex(
        Settings settings,
        IServiceProvider services,
        string message,
        CancellationToken ct)
    {
        AnsiConsole.WriteLine(message);
        await RunIndex(settings, services, progress: null, embeddingProgress: null, ct);
        AnsiConsole.WriteLine("Watch mode recovery completed.");
    }

    private static string GetWatchPath(IServiceProvider services)
    {
        var repositoryWorkspace = services.GetService<IRepositoryWorkspace>();
        if (repositoryWorkspace is not null)
        {
            return repositoryWorkspace.RootPath;
        }

        var options = services.GetRequiredService<IOptions<SharpSenseCliOptions>>().Value;
        return string.IsNullOrWhiteSpace(options.RepositoryRoot)
            ? throw new InvalidOperationException("A repository root must be configured before watch mode can start.")
            : options.RepositoryRoot;
    }

    private sealed class IndexingFailedException : Exception;
}
