using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;
using System.Collections.Concurrent;

namespace SharpSense.Application.Tests.Indexing;

public sealed class AnalysisNotificationsTests
{
    [Fact]
    public async Task WhenConcurrentWorkers_ThenReportOrderedSourceActivityAndCommitOnlyAfterPersistence()
    {
        var ct = TestContext.Current.CancellationToken;
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

        var task = fixture.Full.Handle(
            new IndexWorkspaceCommand(Notifier: fixture.Notifier),
            ct);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            fixture.Notifier.Events.Should().NotContain(item => item.Kind == AnalysisNotificationKind.Committed);
        }
        finally
        {
            release.TrySetResult();
        }
        (await task).IsSuccess.Should().BeTrue();

        var events = fixture.Notifier.Events.ToArray();
        events[0].Kind.Should().Be(AnalysisNotificationKind.Started);
        events
            .Select(item => item.OperationId)
            .Distinct().Should().ContainSingle();
        events
            .Select(item => item.Sequence).Should().Equal(Enumerable.Range(1, events.Length)
                .Select(value => (long)value));
        fixture.Notifier.MaximumConcurrent.Should().Be(1);
        events.Count(item => item.Kind == AnalysisNotificationKind.SourceStarted).Should().Be(3);
        events.Count(item => item.Kind == AnalysisNotificationKind.SourceProgress).Should().Be(30);
        events
            .Where(item => item.Kind == AnalysisNotificationKind.SourceProgress).Should().AllSatisfy(item =>
            {
                item.Source.Should().NotBeNull();
                item.Phase.Should().Be(AnalysisPhase.Extraction);
            });
        var committed = events[^1];
        committed.Kind.Should().Be(AnalysisNotificationKind.Committed);
        committed.Summary.Should().Be(new AnalysisSummary(0, 3, 0, 1, 3, 0, 0, 0, 0));
    }

    [Fact]
    public async Task WhenDocumentation_ThenReuseProducesSeparateIncrementalOperationWithReuseCounts()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        (await fixture.Full.Handle(
            new IndexWorkspaceCommand(Notifier: fixture.Notifier),
            ct)).IsSuccess.Should().BeTrue();
        var firstId = fixture.Notifier.Events.Last().OperationId;

        var result = await fixture.Update.Handle(
            new UpdateWorkspaceFilesCommand(
                [new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: "docs/guide.md")],
                Notifier: fixture.Notifier),
            ct);

        result.IsSuccess.Should().BeTrue();
        var operation = fixture.Notifier.Events
            .Where(item => item.OperationId != firstId)
            .ToArray();
        operation.Should().ContainSingle(item => item.Kind == AnalysisNotificationKind.Started);
        operation.Should().AllSatisfy(item => item.OperationKind.Should().Be(AnalysisOperationKind.Incremental));
        operation.Count(item => item.Kind == AnalysisNotificationKind.SourceReused).Should().Be(2);
        operation[^1].Summary!.ExtractedSources.Should().Be(1);
        operation[^1].Summary!.ReusedSources.Should().Be(2);
    }

    [Fact]
    public async Task WhenIgnoredWatchBatch_ThenHasNoCommitOrExtraction()
    {
        using var fixture = new Fixture();

        var result = await fixture.Update.Handle(
            new UpdateWorkspaceFilesCommand(
                [],
                Notifier: fixture.Notifier),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.IndexCommitted.Should().BeFalse();
        fixture.Notifier.Events
            .Select(item => item.Kind).Should().Equal([AnalysisNotificationKind.Started, AnalysisNotificationKind.Ignored]);
        fixture.Repository.Verify(
            repository => repository.ReplaceWorkspace(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenPersistenceFailureOrCancellation_ThenNeverEmitsCommitted(bool cancelled)
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

        var result = await fixture.Full.Handle(
            new IndexWorkspaceCommand(Notifier: fixture.Notifier),
            cancellation.Token);

        result.IsFailed.Should().BeTrue();
        fixture.Notifier.Events.Should().NotContain(item => item.Kind == AnalysisNotificationKind.Committed);
        fixture.Notifier.Events.Last().Kind.Should().Be(cancelled ? AnalysisNotificationKind.Cancelled : AnalysisNotificationKind.Failed);
    }

    [Fact]
    public async Task WhenLateSourceAndEmbedding_ThenReportsCannotChangeCompletedOperation()
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
        fixture.Embeddings
            .Setup(generator => generator.GenerateBatch(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<EmbeddingGenerationProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, IProgress<EmbeddingGenerationProgress>?, CancellationToken>((
                texts,
                progress,
                _) =>
            {
                embeddingProgress = progress;
                foreach (var previous in extractionProgress)
                {
                    previous.Report(new IndexingProgress("Late source report", 1, 1));
                }
                progress!.Report(new EmbeddingGenerationProgress("Generating", 3, 3));

                return Task.FromResult<IReadOnlyList<TextEmbedding>>(texts
                    .Select(text => new TextEmbedding(text, [1f]))
                    .ToArray());
            });

        (await fixture.Full.Handle(
            new IndexWorkspaceCommand(Notifier: fixture.Notifier),
            TestContext.Current.CancellationToken)).IsSuccess.Should().BeTrue();
        var eventCount = fixture.Notifier.Events.Count;
        foreach (var previous in extractionProgress)
        {
            previous.Report(new IndexingProgress("After commit", 1, 1));
        }
        embeddingProgress!.Report(new EmbeddingGenerationProgress("After commit", 3, 3));

        fixture.Notifier.Events.Count.Should().Be(eventCount);
        fixture.Notifier.Events.Should().NotContain(item => item.Message == "Late source report");
        fixture.Notifier.Events.Should().ContainSingle(item => item.Kind == AnalysisNotificationKind.EmbeddingProgress);
        fixture.Notifier.Events.Last().Summary!.GeneratedEmbeddings.Should().Be(3);
    }

    [Fact]
    public async Task WhenWatchingWorkspace_ThenEmbeddingsAndDiagnosticsReachNotifier()
    {
        using var fixture = new Fixture();
        fixture.Options.SkipEmbeddings = false;
        fixture.Workers[0].Graph = fixture.Workers[0].Graph with
        {
            Diagnostics = Enumerable.Range(0, 75)
                .Select(index => $"Warning {index}")
                .ToArray()
        };
        fixture.Embeddings
            .Setup(generator => generator.GenerateBatch(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IProgress<EmbeddingGenerationProgress>>(),
                It.IsAny<CancellationToken>()))
            .Returns<IEnumerable<string>, IProgress<EmbeddingGenerationProgress>?, CancellationToken>((
                texts,
                progress,
                _) =>
            {
                progress!.Report(new EmbeddingGenerationProgress("Generating", 3, 3));

                return Task.FromResult<IReadOnlyList<TextEmbedding>>(texts
                    .Select(text => new TextEmbedding(text, [1f]))
                    .ToArray());
            });

        var result = await fixture.Update.Handle(
            new UpdateWorkspaceFilesCommand(
                [new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: "src/Code.cs")],
                Notifier: fixture.Notifier),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        fixture.Notifier.Events.Should().ContainSingle(item => item.Kind == AnalysisNotificationKind.Started);
        fixture.Notifier.Events.Should().ContainSingle(item => item.Kind == AnalysisNotificationKind.EmbeddingProgress);
        fixture.Notifier.Events.Count(item => item.Kind == AnalysisNotificationKind.Diagnostic).Should().Be(50);
        fixture.Notifier.Events.Last().Summary!.DiagnosticCount.Should().Be(75);
        fixture.Notifier.Events.Last().Summary!.GeneratedEmbeddings.Should().Be(3);
    }

    [Fact]
    public async Task WhenBrokenObserver_ThenDoesNotAbortAnalysisOrMisreportSuccessfulCommit()
    {
        using var fixture = new Fixture();
        fixture.Notifier.Throw = true;

        var result = await fixture.Full.Handle(
            new IndexWorkspaceCommand(Notifier: fixture.Notifier),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        fixture.Repository.Verify(
            repository => repository.ReplaceWorkspace(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()),
            Times.Once);
        fixture.Notifier.Events.Last().Kind.Should().Be(AnalysisNotificationKind.Committed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenWorkerFailureOrCancellationTerminatesOnlyAfterOtherWorkers_ThenHaveJoined(bool cancelled)
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

        var result = await fixture.Full.Handle(
            new IndexWorkspaceCommand(Notifier: fixture.Notifier),
            cancellation.Token);

        result.IsFailed.Should().BeTrue();
        siblingFinished.Should().BeTrue();
        fixture.Notifier.Events.Last().Kind.Should().Be(cancelled ? AnalysisNotificationKind.Cancelled : AnalysisNotificationKind.Failed);
        fixture.Notifier.Events.Should().NotContain(item => item.Kind == AnalysisNotificationKind.Committed);
    }

    [Fact]
    public async Task WhenEmptySelectedWorkspace_ThenReportsFailureWithoutStartingWorkers()
    {
        using var fixture = new Fixture();
        fixture.Options.WorkspaceSources = [];

        var result = await fixture.Full.Handle(
            new IndexWorkspaceCommand(Notifier: fixture.Notifier),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        fixture.Notifier.Events
            .Select(item => item.Kind).Should().Equal([AnalysisNotificationKind.Started, AnalysisNotificationKind.Failed]);
    }

    private sealed class Fixture : IDisposable
    {
        public Mock<IKnowledgeGraphRepository> Repository { get; } = new(MockBehavior.Strict);
        public Mock<IEmbeddingGenerator> Embeddings { get; } = new(MockBehavior.Strict);
        public Worker[] Workers { get; } = [new(WorkspaceSourceKind.CSharp), new(WorkspaceSourceKind.TypeScript), new(WorkspaceSourceKind.Markdown)];
        public RecordingNotifier Notifier { get; } = new();
        public WorkspaceExecutionOptions Options { get; } = new()
        {
            WorkspaceId = "workspace",
            RepositoryRoot = "/repo",
            SkipEmbeddings = true,
            DisableEmbeddingCache = true,
            WorkspaceSources =
            [
                new(WorkspaceSourceKind.CSharp, "App.sln"),
                new(WorkspaceSourceKind.TypeScript, "frontend"),
                new(WorkspaceSourceKind.Markdown, "docs/**/*.md")
            ]
        };
        public Func<CancellationToken, Task> Commit { get; set; } = _ => Task.CompletedTask;
        public IndexWorkspaceCommandHandler Full { get; }
        public UpdateWorkspaceFilesCommandHandler Update { get; }
        private readonly WorkspaceExtractionCoordinator _coordinator;

        public Fixture()
        {
            var paths = new WorkspacePaths();
            _coordinator = new(
                Workers,
                paths,
                Mock.Of<IWorkspaceChangeFilter>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance);
            Repository
                .Setup(repository => repository.ReplaceWorkspace(
                    It.IsAny<ExtractedNodes>(),
                    It.IsAny<CancellationToken>()))
                .Returns<ExtractedNodes, CancellationToken>((_, token) => Commit(token));
            var options = Microsoft.Extensions.Options.Options.Create(Options);
            Full = new IndexWorkspaceCommandHandler(
                Embeddings.Object,
                Repository.Object,
                paths,
                options,
                _coordinator,
                Mock.Of<GraphStats.Abstractions.IIndexRunStore>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance);
            Update = new UpdateWorkspaceFilesCommandHandler(
                Full,
                Mock.Of<IWorkspaceChangeFilter>(filter => filter.IsRelevant(It.IsAny<IReadOnlyList<WorkspaceFileChange>>()) == true));
        }

        public void Dispose() => _coordinator.Dispose();
    }

    private sealed class Worker(WorkspaceSourceKind kind) : ILanguageExtractor
    {
        public WorkspaceSourceKind SourceKind => kind;
        public ExtractedNodes Graph { get; set; } = new(
            [],
            [
                new IndexedCodeNode(
                    kind.ToString(),
                    null,
                    kind.ToString(),
                    kind.ToString(),
                    kind == WorkspaceSourceKind.Markdown ? NodeType.Document : NodeType.Method,
                    "src/" + kind,
                    1,
                    2,
                    string.Empty,
                    kind.ToString())
            ],
            [],
            [],
            CanReuseForDocumentationChanges: true);
        public Func<ExtractionContext, CancellationToken, Task<Result<ExtractedNodes>>>? OnExtract { get; set; }
        public Task<Result<ExtractedNodes>> Extract(ExtractionContext context, CancellationToken ct)
            => OnExtract?.Invoke(context, ct) ?? Task.FromResult(Result.Ok(Graph));
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
        public string ToRepositoryRelativePath(string? path) => Path.GetRelativePath(
            RootPath,
            GetRequiredTargetPath(path!));
        public bool TryToRepositoryRelativePath(string? path, out string relativePath)
        {
            relativePath = ToRepositoryRelativePath(path);

            return !relativePath.StartsWith("..", StringComparison.Ordinal);
        }
    }
}
