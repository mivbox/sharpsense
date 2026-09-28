using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Indexing;

public sealed class IndexingDiagnosticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenIndexCommits_ThenRecordsSuccessAfterCommitWithEmbeddingCountsAndPhaseTimes(bool incremental)
    {
        var fixture = new Fixture();
        var cached = Node("Cached") with
        {
            VectorEmbedding = [1f, 2f]
        };
        var changed = Node("Changed");
        fixture.Options.SkipEmbeddings = false;
        fixture.Extraction = new ExtractedNodes(
            [],
            [cached with
            {
                VectorEmbedding = null
            }, changed],
            [],
            []);
        fixture.Repository.Setup(repository => repository.GetPersistedCodeNodes(It.IsAny<CancellationToken>()))
            .ReturnsAsync([cached]);
        fixture.Embeddings.Setup(generator => generator.GenerateBatch(
            It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[]
            {
                changed.SearchText
            })),
            null,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TextEmbedding(changed.SearchText, [3f, 4f])]);
        var enteredCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCommit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Repository.Setup(repository => repository.ReplaceWorkspace(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                enteredCommit.SetResult(true);
                await releaseCommit.Task;
            });

        var indexing = fixture.Handle(incremental, TestContext.Current.CancellationToken);
        await enteredCommit.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.Store.Runs.Should().BeEmpty();
        releaseCommit.SetResult(true);
        var result = await indexing;

        result.IsSuccess.Should().BeTrue();
        var run = fixture.Store.Runs.Should().ContainSingle().Which;
        run.Outcome.Should().Be("succeeded");
        run.Kind.Should().Be(incremental ? "incremental" : "full");
        run.Scope.Should().Be("/repo");
        run.ExtractedNodeCount.Should().Be(2L);
        run.ReusedEmbeddingCount.Should().Be(1L);
        run.GeneratedEmbeddingCount.Should().Be(1L);
        (run.CompletedAt >= run.StartedAt).Should().BeTrue();
        (run.DurationMs >= 0).Should().BeTrue();
        foreach (var phase in new[]
        {
            "extraction",
            "embeddings",
            "persistence"
        })
        {
            run.Phases!.Should().Contain(timing => timing.Name == phase && timing.DurationMs >= 0);
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

        result.IsFailed.Should().BeTrue();
        var run = fixture.Store.Runs.Should().ContainSingle().Which;
        run.Outcome.Should().Be("failed");
        var diagnostic = run.Diagnostics!.Should().ContainSingle().Which;
        diagnostic.FilePath.Should().Be("src/broken.ts");
        diagnostic.Message.Should().Be("Invalid source syntax");
        string.IsNullOrWhiteSpace(diagnostic.Suggestion).Should().BeFalse();
        fixture.Repository.Verify(
            repository => repository.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenGraphCommitFails_ThenRecordsFailureWithoutReportingSuccess(bool incremental)
    {
        var fixture = new Fixture();
        fixture.Repository.Setup(repository => repository.ReplaceWorkspace(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Database write failed"));

        var result = await fixture.Handle(incremental, TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var run = fixture.Store.Runs.Should().ContainSingle().Which;
        run.Outcome.Should().Be("failed");
        run.Diagnostics!.Should().Contain(diagnostic => diagnostic.Message == "Database write failed");
        run.Phases!.Should().Contain(timing => timing.Name == "persistence");
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

        result.IsFailed.Should().BeTrue();
        var run = fixture.Store.Runs.Should().ContainSingle().Which;
        run.Outcome.Should().Be("cancelled");
        fixture.Store.WasCancelledAtRecord.Should().BeFalse();
        fixture.Store.RecordTokenWasCancellable.Should().BeTrue();
        fixture.Repository.Verify(
            repository => repository.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenDiagnosticsCannotBeSaved_ThenPreservesSuccessfulGraphResult()
    {
        var fixture = new Fixture();
        fixture.Store.FailRecording = true;

        var result = await fixture.Handle(incremental: false, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        fixture.Store.Runs.Should().ContainSingle().Which.Outcome.Should().Be("succeeded");
        fixture.Repository.Verify(
            repository => repository.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WhenIncrementalBatchIsEmpty_ThenDoesNotRecordAnIndexSuccess()
    {
        var fixture = new Fixture();

        var result = await fixture.CreateIncrementalHandler()
            .Handle(new UpdateWorkspaceFilesCommand([]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Store.Runs.Should().BeEmpty();
        fixture.Extractor.Verify(
            extractor => extractor.Extract(
                It.IsAny<ExtractionContext>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        fixture.Repository.Verify(
            repository => repository.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static IndexedCodeNode Node(string name)
        => new(
            "code:" + name,
            "project:App",
            "App." + name,
            name,
            NodeType.Method,
            "src/Feature.cs",
            1,
            3,
            string.Empty,
            name,
            "body:" + name);

    private sealed class Fixture
    {
        public Mock<ILanguageExtractor> Extractor
        {
            get;
        } = new(MockBehavior.Strict);
        public Mock<IKnowledgeGraphRepository> Repository
        {
            get;
        } = new(MockBehavior.Strict);
        public Mock<IEmbeddingGenerator> Embeddings
        {
            get;
        } = new(MockBehavior.Strict);
        public Mock<IIndexingWorkspacePaths> Paths
        {
            get;
        } = new(MockBehavior.Strict);
        public WorkspaceExecutionOptions Options
        {
            get;
        } = new()
        {
            WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "App.sln")],
            RepositoryRoot = "/repo",
            SkipEmbeddings = true
        };
        public RecordingStore Store
        {
            get;
        } = new();
        public Result<ExtractedNodes> Extraction
        {
            get; set;
        } = Result.Ok(new ExtractedNodes([], [], [], []));

        public Fixture()
        {
            Extractor.SetupGet(extractor => extractor.SourceKind)
                .Returns(WorkspaceSourceKind.CSharp);
            Extractor.Setup(extractor => extractor.Extract(It.IsAny<ExtractionContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Extraction);
            Repository.Setup(repository => repository.ReplaceWorkspace(It.IsAny<ExtractedNodes>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Paths.SetupGet(paths => paths.RootPath)
                .Returns("/repo");
            Paths.Setup(paths => paths.GetRequiredTargetPath("App.sln"))
                .Returns("/repo/App.sln");
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
            => new UpdateWorkspaceFilesCommandHandler(
                new IndexWorkspaceCommandHandler(
                    Embeddings.Object,
                    Repository.Object,
                    Paths.Object,
                    Microsoft.Extensions.Options.Options.Create(Options),
                    new WorkspaceExtractionCoordinator(
                        [Extractor.Object],
                        Paths.Object,
                        Moq.Mock.Of<IWorkspaceChangeFilter>(),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance),
                    Store,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance),
                Mock.Of<IWorkspaceChangeFilter>(filter => filter.IsRelevant(It.IsAny<IReadOnlyList<WorkspaceFileChange>>()) == true));

        public async Task<ResultBase> Handle(bool incremental, CancellationToken ct = default)
        {
            if (incremental)
            {
                return await CreateIncrementalHandler().Handle(
                    new UpdateWorkspaceFilesCommand(
                        [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/src/Feature.cs")]),
                    ct);
            }

            var handler = new IndexWorkspaceCommandHandler(
                Embeddings.Object,
                Repository.Object,
                Paths.Object,
                Microsoft.Extensions.Options.Options.Create(Options),
                new WorkspaceExtractionCoordinator(
                    [Extractor.Object],
                    Paths.Object,
                    Moq.Mock.Of<IWorkspaceChangeFilter>(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance),
                Store,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance);

            return await handler.Handle(new IndexWorkspaceCommand(), ct);
        }
    }

    private sealed class RecordingStore : IIndexRunStore
    {
        public List<IndexRunSummary> Runs
        {
            get;
        } = [];
        public bool WasCancelledAtRecord
        {
            get; private set;
        }
        public bool RecordTokenWasCancellable
        {
            get; private set;
        }
        public bool FailRecording
        {
            get; set;
        }

        public Task Record(IndexRunSummary run, CancellationToken ct)
        {
            Runs.Add(run);
            WasCancelledAtRecord = ct.IsCancellationRequested;
            RecordTokenWasCancellable = ct.CanBeCanceled;

            return FailRecording ? Task.FromException(new IOException("Diagnostics unavailable")) : Task.CompletedTask;
        }
    }
}
