using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Features.Indexing;
using SharpSense.Application.Features.Indexing.IndexSolution;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Cli.Analyze;

[UsedImplicitly]
internal sealed class AnalyzeCommand : AbstractAsyncCommand<AnalyzeCommand.Settings>
{
    [UsedImplicitly]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<solution-path>")]
        public string SolutionPath { get; init; } = string.Empty;

        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(SolutionPath)
                ? ValidationResult.Error("A solution path is required.")
                : ValidationResult.Success();
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        var workspacePath = !string.IsNullOrWhiteSpace(settings.RepositoryRoot)
            ? settings.RepositoryRoot
            : Environment.CurrentDirectory;

        services.AddRepositoryWorkspace(workspacePath);
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
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<IndexSolutionCommand>>();

        await AnsiConsole.Console.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async progressContext =>
            {
                var indexingTask = progressContext.AddTask("Loading solution...");
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

                await handler.HandleAsync(
                    new IndexSolutionCommand(
                        settings.SolutionPath,
                        Progress: progress,
                        EmbeddingProgress: embeddingProgress),
                    ct);

                indexingTask.Description = "Indexing complete.";
                indexingTask.Value = 100;

                if (Volatile.Read(ref embeddingsUpdated) == 0)
                {
                    embeddingsTask.Description = "No embeddings generated.";
                }

                embeddingsTask.Value = 100;
            });

        AnsiConsole.WriteLine($"Indexed {settings.SolutionPath}.");
        return 0;
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
}
