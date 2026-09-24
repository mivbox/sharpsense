using FluentResults;
using Moq;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Features.Indexing;

public sealed class IndexingDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenIndexCommits_ThenRecordsSuccessAfterCommitWithEmbeddingCountsAndPhaseTimes(bool incremental)
    {
        var fixture = new Fixture();
        var cached = Node("Cached") with { VectorEmbedding = [1f, 2f] };
        var changed = Node("Changed");
        fixture.Options.SkipEmbeddings = false;
        fixture.Extraction = new ExtractedNodes([], [cached with { VectorEmbedding = null }, changed], [], []);
        fixture.Repository.Setup(repository => repository.GetPersistedCodeNodes(It.IsAny<CancellationToken>()))
            .ReturnsAsync([cached]);
        fixture.Embeddings.Setup(generator => generator.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { changed.SearchText })),
                null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TextEmbedding(changed.SearchText, [3f, 4f])]);
        var enteredCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Repository.Setup(repository => repository.ReplaceTarget(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enteredCommit.SetResult(true);
                await releaseCommit.Task;
            });

        var indexing = fixture.Handle(incremental, TestContext.Current.CancellationToken);
        await enteredCommit.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty(fixture.Store.Runs);
        releaseCommit.SetResult(true);
        var result = await indexing;

        Assert.True(result.IsSuccess);
        var run = Assert.Single(fixture.Store.Runs);
        Assert.Equal("succeeded", run.Outcome);
        Assert.Equal(incremental ? "incremental" : "full", run.Kind);
        Assert.Equal("/repo/App.sln", run.Scope);
        Assert.Equal(2L, run.ExtractedNodeCount);
        Assert.Equal(1L, run.ReusedEmbeddingCount);
        Assert.Equal(1L, run.GeneratedEmbeddingCount);
        Assert.True(run.CompletedAt >= run.StartedAt);
        Assert.True(run.DurationMs >= 0);
        foreach (var phase in new[] { "extraction", "embeddings", "persistence" })
        {
            Assert.Contains(run.Phases!, timing => timing.Name == phase && timing.DurationMs >= 0);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenExtractorFails_ThenRecordsFailedFileWithoutReplacingGraph(bool incremental)
    {
        var fixture = new Fixture
        {
            Extraction = Result.Fail<ExtractedNodes>(new Error("Invalid source syntax")
                .WithMetadata("filePath", "src/broken.ts"))
        };

        var result = await fixture.Handle(incremental, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        var run = Assert.Single(fixture.Store.Runs);
        Assert.Equal("failed", run.Outcome);
        var diagnostic = Assert.Single(run.Diagnostics!);
        Assert.Equal("src/broken.ts", diagnostic.FilePath);
        Assert.Equal("Invalid source syntax", diagnostic.Message);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Suggestion));
        fixture.Repository.Verify(repository => repository.ReplaceTarget(
            It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenGraphCommitFails_ThenRecordsFailureWithoutReportingSuccess(bool incremental)
    {
        var fixture = new Fixture();
        fixture.Repository.Setup(repository => repository.ReplaceTarget(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Database write failed"));

        var result = await fixture.Handle(incremental, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailed);
        var run = Assert.Single(fixture.Store.Runs);
        Assert.Equal("failed", run.Outcome);
        Assert.Contains(run.Diagnostics!, diagnostic => diagnostic.Message == "Database write failed");
        Assert.Contains(run.Phases!, timing => timing.Name == "persistence");
    }

    [Fact]
    public async Task WhenIncrementalIndexIsCancelled_ThenRecordsCancellationUsingIndependentToken()
    {
        using var cancellation = new CancellationTokenSource();
        var fixture = new Fixture();
        fixture.Extractor.Setup(extractor => extractor.Extract(It.IsAny<ExtractionContext>(), It.IsAny<CancellationToken>()))
            .Returns<ExtractionContext, CancellationToken>((_, token) => Task.FromCanceled<Result<ExtractedNodes>>(token));
        await cancellation.CancelAsync();

        var result = await fixture.Handle(incremental: true, cancellation.Token);

        Assert.True(result.IsFailed);
        var run = Assert.Single(fixture.Store.Runs);
        Assert.Equal("cancelled", run.Outcome);
        Assert.False(fixture.Store.WasCancelledAtRecord);
        Assert.True(fixture.Store.RecordTokenWasCancellable);
        fixture.Repository.Verify(repository => repository.ReplaceTarget(
            It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenDiagnosticsCannotBeSaved_ThenPreservesSuccessfulGraphResult()
    {
        var fixture = new Fixture();
        fixture.Store.FailRecording = true;

        var result = await fixture.Handle(incremental: false, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal("succeeded", Assert.Single(fixture.Store.Runs).Outcome);
        fixture.Repository.Verify(repository => repository.ReplaceTarget(
            It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenIncrementalBatchIsEmpty_ThenDoesNotRecordAnIndexSuccess()
    {
        var fixture = new Fixture();

        var result = await fixture.CreateIncrementalHandler().Handle(new UpdateWorkspaceFilesCommand([]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Store.Runs);
        fixture.Extractor.Verify(extractor => extractor.Extract(
            It.IsAny<ExtractionContext>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Repository.Verify(repository => repository.ReplaceTarget(
            It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static IndexedCodeNode Node(string name)
        => new("code:" + name, "project:App", "App." + name, name, NodeType.Method,
            "src/Feature.cs", 1, 3, string.Empty, name, "body:" + name);

    private sealed class Fixture
    {
        public Mock<ILanguageExtractor> Extractor { get; } = new(MockBehavior.Strict);
        public Mock<IKnowledgeGraphRepository> Repository { get; } = new(MockBehavior.Strict);
        public Mock<IEmbeddingGenerator> Embeddings { get; } = new(MockBehavior.Strict);
        public Mock<IIndexingWorkspacePaths> Paths { get; } = new(MockBehavior.Strict);
        public SharpSenseCliOptions Options { get; } = new() { TargetPath = "App.sln", RepositoryRoot = "/repo", SkipEmbeddings = true };
        public RecordingStore Store { get; } = new();
        public Result<ExtractedNodes> Extraction { get; set; } = Result.Ok(new ExtractedNodes([], [], [], []));

        public Fixture()
        {
            Extractor.SetupGet(extractor => extractor.ExtractorName).Returns("fixture");
            Extractor.Setup(extractor => extractor.Extract(It.IsAny<ExtractionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Extraction);
            Repository.Setup(repository => repository.ReplaceTarget(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Paths.SetupGet(paths => paths.RootPath).Returns("/repo");
            Paths.Setup(paths => paths.GetRequiredTargetPath("App.sln")).Returns("/repo/App.sln");
            Paths.Setup(paths => paths.ToRepositoryRelativePath(It.IsAny<string>()))
                .Returns((string? path) => path!.Replace("/repo/", string.Empty, StringComparison.Ordinal));
            Paths.Setup(paths => paths.TryToRepositoryRelativePath(It.IsAny<string>(), out It.Ref<string>.IsAny))
                .Returns((string? path, out string relativePath) =>
                {
                    relativePath = path!.Replace("/repo/", string.Empty, StringComparison.Ordinal);
                    return true;
                });
        }

        public UpdateWorkspaceFilesCommandHandler CreateIncrementalHandler()
            => new([Extractor.Object], Embeddings.Object, Repository.Object, Paths.Object,
                new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
                Microsoft.Extensions.Options.Options.Create(Options), Store);

        public async Task<ResultBase> Handle(bool incremental, CancellationToken ct = default)
        {
            if (incremental)
            {
                return await CreateIncrementalHandler().Handle(new UpdateWorkspaceFilesCommand(
                    [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/src/Feature.cs")]), ct);
            }

            var handler = new IndexTargetCommandHandler([Extractor.Object], Embeddings.Object,
                Repository.Object, Paths.Object, Microsoft.Extensions.Options.Options.Create(Options), Store);
            return await handler.Handle(new IndexTargetCommand(), ct);
        }
    }

    private sealed class RecordingStore : IIndexRunStore
    {
        public List<IndexRunSummary> Runs { get; } = [];
        public bool WasCancelledAtRecord { get; private set; }
        public bool RecordTokenWasCancellable { get; private set; }
        public bool FailRecording { get; set; }

        public Task RecordAsync(IndexRunSummary run, CancellationToken ct)
        {
            Runs.Add(run);
            WasCancelledAtRecord = ct.IsCancellationRequested;
            RecordTokenWasCancellable = ct.CanBeCanceled;
            return FailRecording ? Task.FromException(new IOException("Diagnostics unavailable")) : Task.CompletedTask;
        }
    }
}
