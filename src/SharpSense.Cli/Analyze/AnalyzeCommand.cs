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
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Serilog;
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
        [CommandArgument(0, "<target-path>")]
        public string TargetPath { get; init; } = string.Empty;

        [CommandOption("--repo-root <path>")] public string? RepositoryRoot { get; init; }

        [CommandOption("--watch")] public bool Watch { get; init; }

        [CommandOption("--no-embeddings")] public bool SkipEmbeddings { get; init; }

        [CommandOption("--no-cache")] public bool DisableEmbeddingCache { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(TargetPath)
                ? ValidationResult.Error("A target path is required.")
                : ValidationResult.Success();
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        var rawRoot = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);
        var targetDirectory = CommandPathResolver.ResolveTargetDirectory(rawRoot, settings.TargetPath);

        services.Configure<SharpSenseCliOptions>(options =>
        {
            options.TargetPath = settings.TargetPath;
            options.RepositoryRoot = rawRoot;
            options.Watch = settings.Watch;
            options.SkipEmbeddings = settings.SkipEmbeddings;
            options.DisableEmbeddingCache = settings.DisableEmbeddingCache;
        });
        services.AddRepositoryWorkspace(rawRoot);
        services.AddSharpSenseConfiguration(targetDirectory);
        services.AddIndexing();
        services.AddEmbeddingsInfrastructure();
        services.AddIndexingInfrastructure();
        services.AddPersistence();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        try
        {
            await IndexTarget(settings, host, ct);

            if (!settings.Watch)
            {
                AnsiConsole.WriteLine($"Indexed {settings.TargetPath}.");
                return 0;
            }

            var watchPath = GetWatchPath(host);
            AnsiConsole.WriteLine($"Watching {watchPath} for C# and Markdown changes...");
            await WatchWorkspace(settings, host, watchPath, ct);
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
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
        IHost host,
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

                await RunIndex(settings, host, progress, embeddingProgress, ct);

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
        IHost host,
        IProgress<IndexingProgress>? progress,
        IProgress<EmbeddingGenerationProgress>? embeddingProgress,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<IndexTargetCommand>>();

        await handler.Handle(
            new IndexTargetCommand(
                progress,
                embeddingProgress),
            ct);
    }

    private static async Task WatchWorkspace(
        Settings settings,
        IHost host,
        string repositoryRoot,
        CancellationToken ct)
    {
        var watcher = host.Services.GetRequiredService<IWorkspaceWatcher>();

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await watcher.Watch(repositoryRoot, ApplyBatchUpdate, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Workspace watcher failed.");
                await RecoverWithFullReindex(
                    settings,
                    host,
                    "Watch mode detected file system watcher errors. Rebuilding the full index...",
                    ct);
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
                await ApplyIncrementalUpdate(settings, host, changedFiles, token);
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
                    host,
                    "Watch mode incremental update failed. Rebuilding the full index...",
                    token);
            }
        }
    }

    private static async Task ApplyIncrementalUpdate(
        Settings settings,
        IHost host,
        IReadOnlyList<WorkspaceFileChange> changedFiles,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateWorkspaceFilesCommand>>();

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

                await handler.Handle(
                    new UpdateWorkspaceFilesCommand(changedFiles, progress),
                    ct);

                updateTask.Value = 100;
            });

        var time = DateTime.Now.ToString("HH:mm:ss");
        AnsiConsole.MarkupLine($"[grey][[{time}]][/] [green]INDEXED[/] {changedFiles.Count} file(s).");
    }

    private static async Task RecoverWithFullReindex(
        Settings settings,
        IHost host,
        string message,
        CancellationToken ct)
    {
        AnsiConsole.WriteLine(message);
        await RunIndex(settings, host, progress: null, embeddingProgress: null, ct);
        AnsiConsole.WriteLine("Watch mode recovery completed.");
    }

    private static string GetWatchPath(IHost host)
    {
        var repositoryWorkspace = host.Services.GetService<IRepositoryWorkspace>();
        if (repositoryWorkspace is not null)
        {
            return repositoryWorkspace.RootPath;
        }

        var options = host.Services.GetRequiredService<IOptions<SharpSenseCliOptions>>().Value;
        return string.IsNullOrWhiteSpace(options.RepositoryRoot)
            ? throw new InvalidOperationException("A repository root must be configured before watch mode can start.")
            : options.RepositoryRoot;
    }
}
