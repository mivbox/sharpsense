using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Application.Tests.Indexing.Support;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Indexing.IndexWorkspace;

public sealed class NamedWorkspaceIndexingTests : IDisposable
{
    private readonly IndexingHandlers _handlers = new();

    public void Dispose() => _handlers.Dispose();

    [Fact]
    public async Task WhenEmptyNamedWorkspace_ThenDoesNotFallBackToRepositoryDiscovery()
    {
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = _handlers.Create(
            repository: repository.Object,
            workspacePaths: new WorkspacePaths(),
            options: new WorkspaceExecutionOptions
            {
                WorkspaceId = Guid.NewGuid().ToString(),
                WorkspaceSources = []
            });

        var result = await handler.Handle(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(error => error.Message.Contains("no selected sources"));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenSelectedSources_ThenCommitOneCombinedGraphAndPreserveCrossProjectEdges()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = Project("first");
        var second = Project("second");
        var dependency = Project("dependency");
        var firstNode = Node("first", first.Id, "first/Feature.cs");
        var secondNode = Node("second", second.Id, "second/Feature.cs");
        var dependencyNode = Node("dependency", dependency.Id, "dependency/Feature.cs");
        var edge = new IndexedDependency(firstNode.CanonicalId, secondNode.CanonicalId, EdgeType.MethodCall);
        var csharp = new RecordingExtractor(
            WorkspaceSourceKind.CSharp,
            context => Result.Ok(new ExtractedNodes(
                [first, second, dependency],
                [firstNode, secondNode, dependencyNode],
                [edge],
                [])));
        var typescript = new RecordingExtractor(
            WorkspaceSourceKind.TypeScript,
            _ => Result.Ok(new ExtractedNodes(
                [],
                [Node("frontend", null, "frontend/index.ts")],
                [],
                [])));
        var markdown = new RecordingExtractor(
            WorkspaceSourceKind.Markdown,
            _ => Result.Ok(new ExtractedNodes(
                [],
                [
                    Node("guide", null, "docs/guide.md") with
                    {
                        NodeType = NodeType.Document
                    }
                ],
                [],
                [])));
        WorkspaceSource[] sources =
        [
            new(WorkspaceSourceKind.CSharp, "first/first.csproj"),
            new(WorkspaceSourceKind.CSharp, "second/second.csproj"),
            new(WorkspaceSourceKind.CSharp, "first/first.csproj"),
            new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json"),
            new(WorkspaceSourceKind.Markdown, "docs/**/*.md"),
            new(WorkspaceSourceKind.Markdown, "README.md")
        ];
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        ExtractedNodes? persisted = null;
        repository
            .Setup(candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct))
            .Callback<ExtractedNodes, CancellationToken>((nodes, _) => persisted = nodes)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(sources, [csharp, typescript, markdown], repository.Object);

        var result = await handler.Handle(new IndexWorkspaceCommand(), ct);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(static error => error.Message)));
        persisted.Should().NotBeNull();
        persisted.Projects.Count.Should().Be(2);
        persisted.CodeNodes.Count.Should().Be(4);
        persisted.CodeNodes.Should().NotContain(node => node.CanonicalId == dependencyNode.CanonicalId);
        persisted.Edges.Should().Contain(edge);
        csharp.Contexts.Count.Should().Be(2);
        var documentationContext = markdown.Contexts.Should().ContainSingle().Which;
        documentationContext.TargetPath.Should().Be("/repo");
        documentationContext.IncludePatterns.Should().Equal(["docs/**/*.md", "README.md"]);
        repository.Verify(
            candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct),
            Times.Once);
    }

    [Fact]
    public async Task WhenALaterSourceFails_ThenExistingWorkspaceRemainsUntouched()
    {
        var successful = new RecordingExtractor(
            WorkspaceSourceKind.TypeScript,
            _ => Result.Ok(new ExtractedNodes(
                [],
                [Node("frontend", null, "frontend/index.ts")],
                [],
                [])));
        var failing = new RecordingExtractor(
            WorkspaceSourceKind.Markdown,
            _ => Result.Fail("Documentation could not be read."));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = CreateHandler(
            [new(WorkspaceSourceKind.TypeScript, "frontend"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")],
            [successful, failing],
            repository.Object);

        var result = await handler.Handle(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(error => error.Message.Contains("Documentation could not be read."));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenOverlappingSourcesWithDifferentImportBindings_ThenFailBeforePersistence()
    {
        var source = Node("shared", null, "shared/index.ts");
        var extractor = new RecordingExtractor(
            WorkspaceSourceKind.TypeScript,
            context => Result.Ok(new ExtractedNodes(
                [],
                [source],
                [new IndexedDependency(source.CanonicalId, context.TargetPath, EdgeType.MethodCall)],
                [])));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = CreateHandler(
            [new(WorkspaceSourceKind.TypeScript, "first/tsconfig.json"), new(WorkspaceSourceKind.TypeScript, "second/tsconfig.json")],
            [extractor],
            repository.Object);

        var result = await handler.Handle(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(error => error.Message.Contains("conflicting definitions"));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenSourcesOverlapEquivalently_ThenDeclarationsAndEdgesAreDeduplicated()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = Node("shared", null, "shared/index.ts");
        var edge = new IndexedDependency(source.CanonicalId, "package:react", EdgeType.MethodCall);
        var extractor = new RecordingExtractor(
            WorkspaceSourceKind.TypeScript,
            _ => Result.Ok(new ExtractedNodes(
                [],
                [source],
                [edge],
                [])));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository
            .Setup(candidate => candidate.ReplaceWorkspace(
                It.Is<ExtractedNodes>(nodes => nodes.CodeNodes.Count == 1 && nodes.Edges.Count == 1),
                ct))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(
            [new(WorkspaceSourceKind.TypeScript, "first/tsconfig.json"), new(WorkspaceSourceKind.TypeScript, "second/tsconfig.json")],
            [extractor],
            repository.Object);

        var result = await handler.Handle(new IndexWorkspaceCommand(), ct);

        result.IsSuccess.Should().BeTrue();
        repository.VerifyAll();
    }

    [Fact]
    public async Task WhenWatchBatch_ThenReconcilesEntireWorkspaceThroughFullIndexer()
    {
        var ct = TestContext.Current.CancellationToken;
        WorkspaceFileChange[] changes = [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/first/Feature.cs")];
        var indexer = new Mock<ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>>(MockBehavior.Strict);
        indexer
            .Setup(candidate => candidate.Handle(
                It.Is<IndexWorkspaceCommand>(command => command.ChangedFiles == changes),
                ct))
            .ReturnsAsync(Result.Ok(new IndexWorkspaceOutcome(2, 7, 5, 1)));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = new UpdateWorkspaceFilesCommandHandler(
            indexer.Object,
            Mock.Of<IWorkspaceChangeFilter>(filter => filter.IsRelevant(It.IsAny<IReadOnlyList<WorkspaceFileChange>>()) == true));

        var result = await handler.Handle(
            new UpdateWorkspaceFilesCommand(changes),
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.CodeNodesPersisted.Should().Be(7);
        indexer.VerifyAll();
        repository.VerifyNoOtherCalls();
    }

    private IndexWorkspaceCommandHandler CreateHandler(
        IReadOnlyList<WorkspaceSource> sources,
        IEnumerable<ILanguageExtractor> extractors,
        IKnowledgeGraphRepository repository)
        => _handlers.Create(
            extractors,
            repository: repository,
            workspacePaths: new WorkspacePaths(),
            options: new WorkspaceExecutionOptions
            {
                RepositoryRoot = "/repo",
                WorkspaceSources = sources,
                SkipEmbeddings = true,
                DisableEmbeddingCache = true
            });

    private static IndexedProject Project(string name) => new(
        name,
        name,
        $"{name}/{name}.csproj",
        "hash");

    private static IndexedCodeNode Node(string id, string? projectId, string path)
        => new(id, projectId, id, id, NodeType.Method, path, 1, 2, "", id);

    private sealed class RecordingExtractor(
        WorkspaceSourceKind kind,
        Func<ExtractionContext, Result<ExtractedNodes>> extract) : ILanguageExtractor
    {
        public List<ExtractionContext> Contexts { get; } = [];
        public WorkspaceSourceKind SourceKind => kind;

        public Task<Result<ExtractedNodes>> Extract(ExtractionContext context, CancellationToken ct)
        {
            Contexts.Add(context);

            return Task.FromResult(extract(context));
        }
    }

    private sealed class WorkspacePaths : IIndexingWorkspacePaths
    {
        public string RootPath => "/repo";
        public string GetRequiredTargetPath(string targetPath) => Path.GetFullPath(targetPath, RootPath);
        public string ToRepositoryRelativePath(string? filePath) => Path.GetRelativePath(
            RootPath,
            GetRequiredTargetPath(filePath!));

        public bool TryToRepositoryRelativePath(string? filePath, out string relativePath)
        {
            relativePath = ToRepositoryRelativePath(filePath);

            return !relativePath.StartsWith("..", StringComparison.Ordinal);
        }
    }
}
