using FluentResults;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Features.Indexing.IndexTarget;

public sealed class NamedWorkspaceIndexingTests
{
    [Fact]
    public async Task EmptyNamedWorkspaceDoesNotFallBackToRepositoryDiscovery()
    {
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = new IndexTargetCommandHandler(
            [], new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object, repository.Object,
            new WorkspacePaths(), Options.Create(new SharpSenseCliOptions
            {
                WorkspaceId = Guid.NewGuid().ToString(),
                TargetPath = "/repo"
            }));

        var result = await handler.Handle(new IndexTargetCommand(), CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, error => error.Message.Contains("no selected sources"));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SelectedSourcesCommitOneCombinedGraphAndPreserveCrossProjectEdges()
    {
        var first = Project("first");
        var second = Project("second");
        var dependency = Project("dependency");
        var firstNode = Node("first", first.Id, "first/Feature.cs");
        var secondNode = Node("second", second.Id, "second/Feature.cs");
        var dependencyNode = Node("dependency", dependency.Id, "dependency/Feature.cs");
        var edge = new IndexedDependency(firstNode.CanonicalId, secondNode.CanonicalId, EdgeType.MethodCall);
        var csharp = new RecordingExtractor(WorkspaceSourceKind.CSharp, context => Result.Ok(new ExtractedNodes(
            [first, second, dependency], [firstNode, secondNode, dependencyNode], [edge], [])));
        var typescript = new RecordingExtractor(WorkspaceSourceKind.TypeScript, _ => Result.Ok(new ExtractedNodes(
            [], [Node("frontend", null, "frontend/index.ts")], [], [])));
        var markdown = new RecordingExtractor(WorkspaceSourceKind.Markdown, _ => Result.Ok(new ExtractedNodes(
            [], [Node("guide", null, "docs/guide.md") with { NodeType = NodeType.Document }], [], [])));
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
        repository.Setup(candidate => candidate.ReplaceTarget(It.IsAny<ExtractedNodes>(), CancellationToken.None))
            .Callback<ExtractedNodes, CancellationToken>((nodes, _) => persisted = nodes)
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(sources, [csharp, typescript, markdown], repository.Object);

        var result = await handler.Handle(new IndexTargetCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(static error => error.Message)));
        Assert.NotNull(persisted);
        Assert.Equal(2, persisted.Projects.Count);
        Assert.Equal(4, persisted.CodeNodes.Count);
        Assert.DoesNotContain(persisted.CodeNodes, node => node.CanonicalId == dependencyNode.CanonicalId);
        Assert.Contains(edge, persisted.Edges);
        Assert.Equal(2, csharp.Contexts.Count);
        var documentationContext = Assert.Single(markdown.Contexts);
        Assert.Equal("/repo", documentationContext.TargetPath);
        Assert.Equal(["docs/**/*.md", "README.md"], documentationContext.IncludePatterns);
        repository.Verify(candidate => candidate.ReplaceTarget(It.IsAny<ExtractedNodes>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task FailureInLaterSourceLeavesExistingWorkspaceUntouched()
    {
        var successful = new RecordingExtractor(WorkspaceSourceKind.TypeScript, _ => Result.Ok(new ExtractedNodes(
            [], [Node("frontend", null, "frontend/index.ts")], [], [])));
        var failing = new RecordingExtractor(WorkspaceSourceKind.Markdown, _ => Result.Fail("Documentation could not be read."));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = CreateHandler(
            [new(WorkspaceSourceKind.TypeScript, "frontend"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")],
            [successful, failing],
            repository.Object);

        var result = await handler.Handle(new IndexTargetCommand(), CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, error => error.Message.Contains("Documentation could not be read."));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OverlappingSourcesWithDifferentImportBindingsFailBeforePersistence()
    {
        var source = Node("shared", null, "shared/index.ts");
        var extractor = new RecordingExtractor(WorkspaceSourceKind.TypeScript, context => Result.Ok(new ExtractedNodes(
            [], [source],
            [new IndexedDependency(source.CanonicalId, context.TargetPath, EdgeType.MethodCall)], [])));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = CreateHandler(
            [new(WorkspaceSourceKind.TypeScript, "first/tsconfig.json"), new(WorkspaceSourceKind.TypeScript, "second/tsconfig.json")],
            [extractor], repository.Object);

        var result = await handler.Handle(new IndexTargetCommand(), CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, error => error.Message.Contains("conflicting definitions"));
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EquivalentOverlappingSourcesDeduplicateDeclarationsAndEdges()
    {
        var source = Node("shared", null, "shared/index.ts");
        var edge = new IndexedDependency(source.CanonicalId, "package:react", EdgeType.MethodCall);
        var extractor = new RecordingExtractor(WorkspaceSourceKind.TypeScript, _ => Result.Ok(new ExtractedNodes(
            [], [source], [edge], [])));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.ReplaceTarget(
                It.Is<ExtractedNodes>(nodes => nodes.CodeNodes.Count == 1 && nodes.Edges.Count == 1), CancellationToken.None))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(
            [new(WorkspaceSourceKind.TypeScript, "first/tsconfig.json"), new(WorkspaceSourceKind.TypeScript, "second/tsconfig.json")],
            [extractor], repository.Object);

        var result = await handler.Handle(new IndexTargetCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.VerifyAll();
    }

    [Fact]
    public async Task WatchBatchReconcilesEntireWorkspaceThroughFullIndexer()
    {
        WorkspaceFileChange[] changes = [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/first/Feature.cs")];
        var indexer = new Mock<ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>>(MockBehavior.Strict);
        indexer.Setup(candidate => candidate.Handle(
                It.Is<IndexTargetCommand>(command => command.ChangedFiles == changes), CancellationToken.None))
            .ReturnsAsync(Result.Ok(new IndexTargetOutcome(2, 7, 5, 1)));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = new UpdateWorkspaceFilesCommandHandler(
            [], new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object,
            repository.Object, new WorkspacePaths(), new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
            Options.Create(new SharpSenseCliOptions { WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "first/first.csproj")] }),
            workspaceIndexer: indexer.Object);

        var result = await handler.Handle(new UpdateWorkspaceFilesCommand(changes), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.CodeNodesPersisted);
        indexer.VerifyAll();
        repository.VerifyNoOtherCalls();
    }

    private static IndexTargetCommandHandler CreateHandler(
        IReadOnlyList<WorkspaceSource> sources,
        IEnumerable<ILanguageExtractor> extractors,
        IKnowledgeGraphRepository repository)
        => new(extractors, new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object, repository,
            new WorkspacePaths(), Options.Create(new SharpSenseCliOptions
            {
                RepositoryRoot = "/repo",
                WorkspaceSources = sources,
                SkipEmbeddings = true,
                DisableEmbeddingCache = true
            }));

    private static IndexedProject Project(string name) => new(name, name, $"{name}/{name}.csproj", "hash");

    private static IndexedCodeNode Node(string id, string? projectId, string path)
        => new(id, projectId, id, id, NodeType.Method, path, 1, 2, "", id);

    private sealed class RecordingExtractor(
        WorkspaceSourceKind kind,
        Func<ExtractionContext, Result<ExtractedNodes>> extract) : ILanguageExtractor
    {
        public List<ExtractionContext> Contexts { get; } = [];
        public WorkspaceSourceKind? SourceKind => kind;
        public string ExtractorName => kind.ToString();

        public Task<Result<ExtractedNodes>> Extract(ExtractionContext context, CancellationToken ct)
        {
            Contexts.Add(context);
            return Task.FromResult(extract(context));
        }

        public Task<Result<ExtractedNodes>> ExtractIncremental(IncrementalExtractionContext context, CancellationToken ct)
            => throw new InvalidOperationException("Named workspace must reconcile complete selections.");
    }

    private sealed class WorkspacePaths : IIndexingWorkspacePaths
    {
        public string RootPath => "/repo";
        public string GetRequiredTargetPath(string targetPath) => Path.GetFullPath(targetPath, RootPath);
        public string ToRepositoryRelativePath(string? filePath) => Path.GetRelativePath(RootPath, GetRequiredTargetPath(filePath!));

        public bool TryToRepositoryRelativePath(string? filePath, out string relativePath)
        {
            relativePath = ToRepositoryRelativePath(filePath);
            return !relativePath.StartsWith("..", StringComparison.Ordinal);
        }
    }
}
