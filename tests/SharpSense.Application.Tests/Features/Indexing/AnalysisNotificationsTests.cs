using System.Collections.Concurrent;
using FluentResults;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Features.Indexing;

public sealed class AnalysisNotificationsTests
{
    [Fact]
    public async Task ConcurrentWorkersReportOrderedSourceActivityAndCommitOnlyAfterPersistence()
    {
        using var fixture = new Fixture();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Commit = async token =>
        {
            started.SetResult();
            await release.Task.WaitAsync(token);
        };
        foreach (var worker in fixture.Workers)
        {
            worker.OnExtract = (context, _) =>
            {
                for (var index = 0; index < 10; index++)
                {
                    context.Progress?.Report(new IndexingProgress("Analyzing declarations", index, 10));
                }

                return Task.FromResult(Result.Ok(worker.Graph));
            };
        }

        var task = fixture.Full.Handle(new IndexTargetCommand(Notifier: fixture.Notifier), TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(fixture.Notifier.Events, item => item.Kind == AnalysisNotificationKind.Committed);
        release.SetResult();
        Assert.True((await task).IsSuccess);

        var events = fixture.Notifier.Events.ToArray();
        Assert.Equal(AnalysisNotificationKind.Started, events[0].Kind);
        Assert.Single(events.Select(item => item.OperationId).Distinct());
        Assert.Equal(Enumerable.Range(1, events.Length).Select(value => (long)value), events.Select(item => item.Sequence));
        Assert.Equal(1, fixture.Notifier.MaximumConcurrent);
        Assert.Equal(3, events.Count(item => item.Kind == AnalysisNotificationKind.SourceStarted));
        Assert.Equal(30, events.Count(item => item.Kind == AnalysisNotificationKind.SourceProgress));
        Assert.All(events.Where(item => item.Kind == AnalysisNotificationKind.SourceProgress), item =>
        {
            Assert.NotNull(item.Source);
            Assert.Equal(AnalysisPhase.Extraction, item.Phase);
        });
        var committed = events[^1];
        Assert.Equal(AnalysisNotificationKind.Committed, committed.Kind);
        Assert.Equal(new AnalysisSummary(0, 3, 0, 1, 3, 0, 0, 0, 0), committed.Summary);
    }

    [Fact]
    public async Task DocumentationReuseProducesSeparateIncrementalOperationWithReuseCounts()
    {
        using var fixture = new Fixture();
        Assert.True((await fixture.Full.Handle(new IndexTargetCommand(Notifier: fixture.Notifier), TestContext.Current.CancellationToken)).IsSuccess);
        var firstId = fixture.Notifier.Events.Last().OperationId;

        var result = await fixture.Update.Handle(new UpdateWorkspaceFilesCommand(
            [new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: "docs/guide.md")],
            Notifier: fixture.Notifier), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        var operation = fixture.Notifier.Events.Where(item => item.OperationId != firstId).ToArray();
        Assert.Single(operation, item => item.Kind == AnalysisNotificationKind.Started);
        Assert.All(operation, item => Assert.Equal(AnalysisOperationKind.Incremental, item.OperationKind));
        Assert.Equal(2, operation.Count(item => item.Kind == AnalysisNotificationKind.SourceReused));
        Assert.Equal(1, operation[^1].Summary!.ExtractedSources);
        Assert.Equal(2, operation[^1].Summary!.ReusedSources);
    }

    [Fact]
    public async Task IgnoredWatchBatchHasNoCommitOrExtraction()
    {
        using var fixture = new Fixture();

        var result = await fixture.Update.Handle(new UpdateWorkspaceFilesCommand([], Notifier: fixture.Notifier), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IndexCommitted);
        Assert.Equal([AnalysisNotificationKind.Started, AnalysisNotificationKind.Ignored], fixture.Notifier.Events.Select(item => item.Kind));
        fixture.Repository.Verify(repository => repository.ReplaceTarget(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistenceFailureOrCancellationNeverEmitsCommitted(bool cancelled)
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        fixture.Commit = async token =>
        {
            if (cancelled)
            {
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            }

            throw new IOException("Commit rolled back");
        };

        var result = await fixture.Full.Handle(new IndexTargetCommand(Notifier: fixture.Notifier), cancellation.Token);

        Assert.True(result.IsFailed);
        Assert.DoesNotContain(fixture.Notifier.Events, item => item.Kind == AnalysisNotificationKind.Committed);
        Assert.Equal(cancelled ? AnalysisNotificationKind.Cancelled : AnalysisNotificationKind.Failed,
            fixture.Notifier.Events.Last().Kind);
    }

    [Fact]
    public async Task LateSourceAndEmbeddingReportsCannotChangeCompletedOperation()
    {
        using var fixture = new Fixture();
        fixture.Options.SkipEmbeddings = false;
        var extractionProgress = new ConcurrentBag<IProgress<IndexingProgress>>();
        IProgress<EmbeddingGenerationProgress>? embeddingProgress = null;
        foreach (var worker in fixture.Workers)
        {
            worker.OnExtract = (context, _) =>
            {
                extractionProgress.Add(context.Progress!);
                return Task.FromResult(Result.Ok(worker.Graph));
            };
        }
        fixture.Embeddings.Setup(generator => generator.GenerateBatch(
                It.IsAny<IEnumerable<string>>(), It.IsAny<IProgress<EmbeddingGenerationProgress>>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, IProgress<EmbeddingGenerationProgress>?, CancellationToken>((texts, progress, _) =>
            {
                embeddingProgress = progress;
                foreach (var previous in extractionProgress)
                {
                    previous.Report(new IndexingProgress("Late source report", 1, 1));
                }
                progress!.Report(new EmbeddingGenerationProgress("Generating", 3, 3));
                return Task.FromResult<IReadOnlyList<TextEmbedding>>(texts.Select(text => new TextEmbedding(text, [1f])).ToArray());
            });

        Assert.True((await fixture.Full.Handle(new IndexTargetCommand(Notifier: fixture.Notifier), TestContext.Current.CancellationToken)).IsSuccess);
        var eventCount = fixture.Notifier.Events.Count;
        foreach (var previous in extractionProgress)
        {
            previous.Report(new IndexingProgress("After commit", 1, 1));
        }
        embeddingProgress!.Report(new EmbeddingGenerationProgress("After commit", 3, 3));

        Assert.Equal(eventCount, fixture.Notifier.Events.Count);
        Assert.DoesNotContain(fixture.Notifier.Events, item => item.Message == "Late source report");
        Assert.Single(fixture.Notifier.Events, item => item.Kind == AnalysisNotificationKind.EmbeddingProgress);
        Assert.Equal(3, fixture.Notifier.Events.Last().Summary!.GeneratedEmbeddings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncrementalEmbeddingsAndDiagnosticsReachNotifier(bool namedWorkspace)
    {
        using var fixture = new Fixture();
        fixture.Options.SkipEmbeddings = false;
        if (!namedWorkspace)
        {
            fixture.Options.WorkspaceSources = [];
            fixture.Options.WorkspaceId = null;
        }
        fixture.Workers[0].Graph = fixture.Workers[0].Graph with
        {
            Diagnostics = Enumerable.Range(0, 75).Select(index => $"Warning {index}").ToArray()
        };
        fixture.Embeddings.Setup(generator => generator.GenerateBatch(
                It.IsAny<IEnumerable<string>>(), It.IsAny<IProgress<EmbeddingGenerationProgress>>(), It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, IProgress<EmbeddingGenerationProgress>?, CancellationToken>((texts, progress, _) =>
            {
                progress!.Report(new EmbeddingGenerationProgress("Generating", 3, 3));
                return Task.FromResult<IReadOnlyList<TextEmbedding>>(texts.Select(text => new TextEmbedding(text, [1f])).ToArray());
            });

        var result = await fixture.Update.Handle(new UpdateWorkspaceFilesCommand(
            [new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: "src/Code.cs")], Notifier: fixture.Notifier),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Single(fixture.Notifier.Events, item => item.Kind == AnalysisNotificationKind.Started);
        Assert.Single(fixture.Notifier.Events, item => item.Kind == AnalysisNotificationKind.EmbeddingProgress);
        Assert.Equal(50, fixture.Notifier.Events.Count(item => item.Kind == AnalysisNotificationKind.Diagnostic));
        Assert.Equal(75, fixture.Notifier.Events.Last().Summary!.DiagnosticCount);
        Assert.Equal(3, fixture.Notifier.Events.Last().Summary!.GeneratedEmbeddings);
    }

    [Fact]
    public async Task BrokenObserverDoesNotAbortAnalysisOrMisreportSuccessfulCommit()
    {
        using var fixture = new Fixture();
        fixture.Notifier.Throw = true;

        var result = await fixture.Full.Handle(new IndexTargetCommand(Notifier: fixture.Notifier), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        fixture.Repository.Verify(repository => repository.ReplaceTarget(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(AnalysisNotificationKind.Committed, fixture.Notifier.Events.Last().Kind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerFailureOrCancellationTerminatesOnlyAfterOtherWorkersHaveJoined(bool cancelled)
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var siblingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var siblingFinished = false;
        fixture.Workers[0].OnExtract = async (_, token) =>
        {
            await siblingStarted.Task.WaitAsync(token);
            if (cancelled)
            {
                await cancellation.CancelAsync();
                token.ThrowIfCancellationRequested();
            }

            return Result.Fail("Source extraction failed");
        };
        fixture.Workers[1].OnExtract = async (_, token) =>
        {
            siblingStarted.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, token);
                return Result.Ok(fixture.Workers[1].Graph);
            }
            finally
            {
                siblingFinished = true;
            }
        };

        var result = await fixture.Full.Handle(new IndexTargetCommand(Notifier: fixture.Notifier), cancellation.Token);

        Assert.True(result.IsFailed);
        Assert.True(siblingFinished);
        Assert.Equal(cancelled ? AnalysisNotificationKind.Cancelled : AnalysisNotificationKind.Failed,
            fixture.Notifier.Events.Last().Kind);
        Assert.DoesNotContain(fixture.Notifier.Events, item => item.Kind == AnalysisNotificationKind.Committed);
    }

    [Fact]
    public async Task EmptySelectedWorkspaceReportsFailureWithoutStartingWorkers()
    {
        using var fixture = new Fixture();
        fixture.Options.WorkspaceSources = [];

        var result = await fixture.Full.Handle(new IndexTargetCommand(Notifier: fixture.Notifier), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        Assert.Equal([AnalysisNotificationKind.Started, AnalysisNotificationKind.Failed], fixture.Notifier.Events.Select(item => item.Kind));
    }

    [Fact]
    public async Task InvalidLegacyTargetClosesOperationBeforePropagatingConfigurationError()
    {
        using var fixture = new Fixture();
        fixture.Options.WorkspaceSources = [];
        fixture.Options.WorkspaceId = null;
        fixture.Options.TargetPath = null;

        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Full.Handle(
            new IndexTargetCommand(Notifier: fixture.Notifier), TestContext.Current.CancellationToken));

        Assert.Equal([AnalysisNotificationKind.Started, AnalysisNotificationKind.Failed], fixture.Notifier.Events.Select(item => item.Kind));
    }

    private sealed class Fixture : IDisposable
    {
        public Mock<IKnowledgeGraphRepository> Repository { get; } = new(MockBehavior.Strict);
        public Mock<IEmbeddingGenerator> Embeddings { get; } = new(MockBehavior.Strict);
        public Worker[] Workers { get; } = [new(WorkspaceSourceKind.CSharp), new(WorkspaceSourceKind.TypeScript), new(WorkspaceSourceKind.Markdown)];
        public RecordingNotifier Notifier { get; } = new();
        public SharpSenseCliOptions Options { get; } = new()
        {
            WorkspaceId = "workspace", RepositoryRoot = "/repo", TargetPath = "App.sln", SkipEmbeddings = true,
            DisableEmbeddingCache = true,
            WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "App.sln"), new(WorkspaceSourceKind.TypeScript, "frontend"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]
        };
        public Func<CancellationToken, Task> Commit { get; set; } = _ => Task.CompletedTask;
        public IndexTargetCommandHandler Full { get; }
        public UpdateWorkspaceFilesCommandHandler Update { get; }
        private readonly WorkspaceExtractionCoordinator _coordinator;

        public Fixture()
        {
            var paths = new WorkspacePaths();
            _coordinator = new(Workers, paths);
            Repository.Setup(repository => repository.ReplaceTarget(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
                .Returns<ExtractedNodes, CancellationToken>((_, token) => Commit(token));
            var options = Microsoft.Extensions.Options.Options.Create(Options);
            Full = new(Workers, Embeddings.Object, Repository.Object, paths, options, workspaceExtraction: _coordinator);
            Update = new(Workers, Embeddings.Object, Repository.Object, paths,
                new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object, options, workspaceIndexer: Full);
        }

        public void Dispose() => _coordinator.Dispose();
    }

    private sealed class Worker(WorkspaceSourceKind kind) : ILanguageExtractor
    {
        public WorkspaceSourceKind? SourceKind => kind;
        public string ExtractorName => kind.ToString();
        public ExtractedNodes Graph { get; set; } = new([], [new IndexedCodeNode(kind.ToString(), null,
            kind.ToString(), kind.ToString(), kind == WorkspaceSourceKind.Markdown ? NodeType.Document : NodeType.Method,
            "src/" + kind, 1, 2, string.Empty, kind.ToString())], [], [], CanReuseForDocumentationChanges: true);
        public Func<ExtractionContext, CancellationToken, Task<Result<ExtractedNodes>>>? OnExtract { get; set; }
        public Task<Result<ExtractedNodes>> Extract(ExtractionContext context, CancellationToken ct)
            => OnExtract?.Invoke(context, ct) ?? Task.FromResult(Result.Ok(Graph));
        public Task<Result<ExtractedNodes>> ExtractIncremental(IncrementalExtractionContext context, CancellationToken ct)
            => Extract(new ExtractionContext(context.TargetPath, context.Progress, context.ChangedFiles), ct);
    }

    private sealed class RecordingNotifier : IAnalysisNotifier
    {
        public ConcurrentQueue<AnalysisNotification> Events { get; } = new();
        public int MaximumConcurrent { get; private set; }
        public bool Throw { get; set; }
        private int _active;

        public void Notify(AnalysisNotification notification)
        {
            MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref _active));
            try
            {
                Events.Enqueue(notification);
                if (Throw)
                {
                    throw new IOException("Renderer unavailable");
                }
                Thread.SpinWait(1_000);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class WorkspacePaths : IIndexingWorkspacePaths
    {
        public string RootPath => "/repo";
        public string GetRequiredTargetPath(string targetPath) => Path.GetFullPath(targetPath, RootPath);
        public string ToRepositoryRelativePath(string? path) => Path.GetRelativePath(RootPath, GetRequiredTargetPath(path!));
        public bool TryToRepositoryRelativePath(string? path, out string relativePath)
        {
            relativePath = ToRepositoryRelativePath(path);
            return !relativePath.StartsWith("..", StringComparison.Ordinal);
        }
    }
}
