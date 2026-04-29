using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
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
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class KnowledgeGraphIndexingTests
{
    [Fact]
    public async Task WhenIndexingTargetWithDocumentNodes_ThenPersistsDocumentNodesAndLinks()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            fullMarkdownNodes: CreateGuideAndReferenceDocuments(),
            incrementalMarkdownNodes: EmptyNodes(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(
            new IndexTargetCommand(),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var documentNodes = await context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => codeNode.NodeType == NodeType.Document)
            .OrderBy(codeNode => codeNode.CanonicalId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var documentEdges = await context.DependencyEdges
            .AsNoTracking()
            .Where(edge => edge.EdgeType == EdgeType.DocumentLink)
            .OrderBy(edge => edge.CallerId)
            .ThenBy(edge => edge.CalleeId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var workspaceTreeNodes = await context.WorkspaceTreeNodes
            .AsNoTracking()
            .OrderBy(node => node.Path)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        documentNodes.Should().HaveCount(2);
        documentNodes.Select(static node => node.CanonicalId)
            .Should()
            .Equal(
                "code:doc:docs/Guide.md#document-root",
                "code:doc:docs/Reference.md#document-root");
        documentNodes.Select(static node => node.RelativeFilePath)
            .Should()
            .Equal(
                "docs/Guide.md",
                "docs/Reference.md");
        documentEdges.Should().ContainSingle();
        documentEdges[0].CallerId.Should().Be("code:doc:docs/Guide.md#document-root");
        documentEdges[0].CalleeId.Should().Be("code:doc:docs/Reference.md#document-root");
        documentEdges[0].EdgeType.Should().Be(EdgeType.DocumentLink);
        workspaceTreeNodes.Should().Contain(node => node.Path == "docs" && node.Kind == WorkspaceTreeNodeKind.Folder);
        workspaceTreeNodes.Should().Contain(node => node.Path == "docs/Guide.md" && node.Kind == WorkspaceTreeNodeKind.File);
        workspaceTreeNodes.Should().Contain(node => node.Path == "docs/Reference.md" && node.Kind == WorkspaceTreeNodeKind.File);
    }

    [Fact]
    public async Task WhenReindexingUnchangedTarget_ThenPreservesPersistedIntegerIds()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            fullMarkdownNodes: CreateGuideAndReferenceDocuments(),
            incrementalMarkdownNodes: EmptyNodes(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexTargetCommand(), TestContext.Current.CancellationToken);
        var persistedIds = await context.CodeNodes
            .AsNoTracking()
            .OrderBy(codeNode => codeNode.CanonicalId)
            .ToDictionaryAsync(
                codeNode => codeNode.CanonicalId,
                codeNode => codeNode.Id,
                TestContext.Current.CancellationToken);

        await indexing.Index(new IndexTargetCommand(), TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var reindexedIds = await context.CodeNodes
            .AsNoTracking()
            .OrderBy(codeNode => codeNode.CanonicalId)
            .ToDictionaryAsync(
                codeNode => codeNode.CanonicalId,
                codeNode => codeNode.Id,
                TestContext.Current.CancellationToken);

        reindexedIds.Should().BeEquivalentTo(persistedIds);
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForModifiedMarkdown_ThenReplacesDocumentNodesAndSearchRows()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            fullMarkdownNodes: CreateGettingStartedDocuments(),
            incrementalMarkdownNodes: CreateIncrementalGuideDocuments(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexTargetCommand(), TestContext.Current.CancellationToken);

        await indexing.UpdateIncremental(
            new UpdateWorkspaceFilesCommand(
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: "/repo/docs/Guide.md")
                ]),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var guideNodes = await context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => codeNode.RelativeFilePath == "docs/Guide.md")
            .OrderBy(codeNode => codeNode.CanonicalId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var oldSearchCount = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM CodeNodeSearch WHERE CanonicalId = {0}",
                "code:doc:docs/Guide.md#getting-started")
            .SingleAsync(TestContext.Current.CancellationToken);
        var newSearchCount = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM CodeNodeSearch WHERE CanonicalId = {0}",
                "code:doc:docs/Guide.md#incremental-guide")
            .SingleAsync(TestContext.Current.CancellationToken);

        guideNodes.Should().Contain(static node => node.CanonicalId == "code:doc:docs/Guide.md#incremental-guide");
        guideNodes.Should().NotContain(static node => node.CanonicalId == "code:doc:docs/Guide.md#getting-started");
        oldSearchCount.Should().Be(0);
        newSearchCount.Should().Be(1);
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForModifiedMarkdown_ThenPreservesInboundEdgesFromUnchangedDocuments()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            fullMarkdownNodes: CreateLinkedDocuments(),
            incrementalMarkdownNodes: CreateUpdatedLinkedDocuments(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexTargetCommand(), TestContext.Current.CancellationToken);

        await indexing.UpdateIncremental(
            new UpdateWorkspaceFilesCommand(
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: "/repo/docs/DocB.md")
                ]),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var inboundEdgeCount = await context.DependencyEdges
            .AsNoTracking()
            .CountAsync(
                edge =>
                    edge.CallerId == "code:doc:docs/DocA.md#document-root" &&
                    edge.CalleeId == "code:doc:docs/DocB.md#document-root" &&
                    edge.EdgeType == EdgeType.DocumentLink,
                TestContext.Current.CancellationToken);

        inboundEdgeCount.Should().Be(1);
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForDeletedMarkdown_ThenRemovesDocumentNodesAndSearchRows()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>(),
            fullMarkdownNodes: CreateLinkedDocuments(),
            incrementalMarkdownNodes: EmptyNodes(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexTargetCommand(), TestContext.Current.CancellationToken);

        await indexing.UpdateIncremental(
            new UpdateWorkspaceFilesCommand(
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Deleted,
                        OldPath: "/repo/docs/DocB.md")
                ]),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var deletedNodeCount = await context.CodeNodes
            .AsNoTracking()
            .CountAsync(
                codeNode => codeNode.RelativeFilePath == "docs/DocB.md",
                TestContext.Current.CancellationToken);
        var searchCount = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM CodeNodeSearch WHERE CanonicalId = {0}",
                "code:doc:docs/DocB.md#document-root")
            .SingleAsync(TestContext.Current.CancellationToken);

        deletedNodeCount.Should().Be(0);
        searchCount.Should().Be(0);
        context.WorkspaceTreeNodes.Should().NotContain(node => node.Path == "docs/DocB.md");
    }

    private static ExtractedNodes CreateGuideAndReferenceDocuments()
        => new(
            [],
            [
                new IndexedCodeNode(
                    "code:doc:docs/Guide.md#document-root",
                    null,
                    "docs/Guide.md#document-root",
                    "Guide",
                    NodeType.Document,
                    "docs/Guide.md",
                    1,
                    1,
                    "See [Reference](./Reference.md)."),
                new IndexedCodeNode(
                    "code:doc:docs/Reference.md#document-root",
                    null,
                    "docs/Reference.md#document-root",
                    "Reference",
                    NodeType.Document,
                    "docs/Reference.md",
                    1,
                    1,
                    "Reference content.")
            ],
            [
                new IndexedDependency(
                    "code:doc:docs/Guide.md#document-root",
                    "code:doc:docs/Reference.md#document-root",
                    EdgeType.DocumentLink)
            ],
            []);

    private static ExtractedNodes CreateGettingStartedDocuments()
        => new(
            [],
            [
                new IndexedCodeNode(
                    "code:doc:docs/Guide.md#getting-started",
                    null,
                    "docs/Guide.md#getting-started",
                    "Getting Started",
                    NodeType.Document,
                    "docs/Guide.md",
                    1,
                    3,
                    "Getting started guide.")
            ],
            [],
            []);

    private static ExtractedNodes CreateIncrementalGuideDocuments()
        => new(
            [],
            [
                new IndexedCodeNode(
                    "code:doc:docs/Guide.md#incremental-guide",
                    null,
                    "docs/Guide.md#incremental-guide",
                    "Incremental Guide",
                    NodeType.Document,
                    "docs/Guide.md",
                    1,
                    2,
                    "Watch mode should refresh this guide.")
            ],
            [],
            []);

    private static ExtractedNodes CreateLinkedDocuments()
        => new(
            [],
            [
                new IndexedCodeNode(
                    "code:doc:docs/DocA.md#document-root",
                    null,
                    "docs/DocA.md#document-root",
                    "DocA",
                    NodeType.Document,
                    "docs/DocA.md",
                    1,
                    1,
                    "See [DocB](./DocB.md)."),
                new IndexedCodeNode(
                    "code:doc:docs/DocB.md#document-root",
                    null,
                    "docs/DocB.md#document-root",
                    "DocB",
                    NodeType.Document,
                    "docs/DocB.md",
                    1,
                    1,
                    "Doc B reference content.")
            ],
            [
                new IndexedDependency(
                    "code:doc:docs/DocA.md#document-root",
                    "code:doc:docs/DocB.md#document-root",
                    EdgeType.DocumentLink)
            ],
            []);

    private static ExtractedNodes CreateUpdatedLinkedDocuments()
        => new(
            [],
            [
                new IndexedCodeNode(
                    "code:doc:docs/DocB.md#document-root",
                    null,
                    "docs/DocB.md#document-root",
                    "DocB",
                    NodeType.Document,
                    "docs/DocB.md",
                    1,
                    1,
                    "Doc B reference content updated.")
            ],
            [],
            []);

    private static ExtractedNodes EmptyNodes()
        => new([], [], [], []);

    private static Mock<IIndexingWorkspacePaths> CreateWorkspacePaths()
    {
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths.Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths.Setup(candidate => candidate.ToRepositoryRelativePath(It.IsAny<string>()))
            .Returns((string? path) => NormalizeRepositoryPath(path));
        workspacePaths.Setup(candidate => candidate.TryToRepositoryRelativePath(It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Returns((string? path, out string relativePath) =>
            {
                relativePath = NormalizeRepositoryPath(path);
                return !string.IsNullOrWhiteSpace(relativePath);
            });
        return workspacePaths;
    }

    private static string NormalizeRepositoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalizedPath = path.Replace('\\', '/');
        return normalizedPath.StartsWith("/repo/", StringComparison.Ordinal)
            ? normalizedPath["/repo/".Length..]
            : normalizedPath;
    }

    private static KnowledgeGraphIndexing CreateIndexing(
        IDbContextFactory<SharpSenseDbContext> dbContextFactory,
        ExtractedNodes fullMarkdownNodes,
        ExtractedNodes incrementalMarkdownNodes,
        Mock<IIndexingWorkspacePaths> workspacePaths)
    {
        var markdownExtractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        markdownExtractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("markdown");
        markdownExtractor.Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullMarkdownNodes);
        markdownExtractor.Setup(candidate => candidate.ExtractIncremental(
                It.IsAny<IncrementalExtractionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(incrementalMarkdownNodes);

        var cSharpExtractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        cSharpExtractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("csharp");
        cSharpExtractor.Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyNodes());
        cSharpExtractor.Setup(candidate => candidate.ExtractIncremental(
                It.IsAny<IncrementalExtractionContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptyNodes());

        return new KnowledgeGraphIndexing(
            [cSharpExtractor.Object, markdownExtractor.Object],
            new NoOpEmbeddingGenerator(),
            new KnowledgeGraphRepository(dbContextFactory),
            workspacePaths.Object,
            Options.Create(new SharpSenseCliOptions
            {
                RepositoryRoot = "/repo",
                TargetPath = "SharpSense.sln",
                SkipEmbeddings = true
            }));
    }

    private sealed class NoOpEmbeddingGenerator : IEmbeddingGenerator
    {
        public Task<TextEmbedding> Generate(string text, CancellationToken ct = default)
            => Task.FromResult(new TextEmbedding(text, []));

        public Task<IReadOnlyList<TextEmbedding>> GenerateBatch(
            IEnumerable<string> texts,
            IProgress<EmbeddingGenerationProgress>? progress,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TextEmbedding>>([]);
    }

    private sealed class KnowledgeGraphIndexing(
        IEnumerable<ILanguageExtractor> extractors,
        IEmbeddingGenerator embeddingGenerator,
        IKnowledgeGraphRepository knowledgeGraphRepository,
        IIndexingWorkspacePaths workspacePaths,
        IOptions<SharpSenseCliOptions> options)
    {
        private readonly IndexTargetCommandHandler _indexTargetHandler = new(
            extractors,
            embeddingGenerator,
            knowledgeGraphRepository,
            workspacePaths,
            options);

        private readonly UpdateWorkspaceFilesCommandHandler _updateWorkspaceFilesHandler = new(
            extractors,
            knowledgeGraphRepository,
            workspacePaths,
            options);

        public Task Index(
            IndexTargetCommand command,
            CancellationToken ct)
            => _indexTargetHandler.Handle(command, ct);

        public Task UpdateIncremental(
            UpdateWorkspaceFilesCommand command,
            CancellationToken ct)
            => _updateWorkspaceFilesHandler.Handle(command, ct);
    }
}
