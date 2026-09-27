using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
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
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Shared;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class KnowledgeGraphIndexingTests
{
    [Fact]
    public async Task WhenIndexingTargetWithDocumentNodes_ThenPersistsDocumentNodesAndLinks()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory(),
            fullMarkdownNodes: CreateGuideAndReferenceDocuments(),
            incrementalMarkdownNodes: EmptyNodes(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(
            new IndexWorkspaceCommand(),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var documentNodeRecords = context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => codeNode.NodeType == NodeType.Document)
            .OrderBy(codeNode => codeNode.Id);
        var documentNodes = await CodeNodeNavigationQueries.ProjectCodeNodes(context, documentNodeRecords)
            .OrderBy(codeNode => codeNode.CanonicalId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var documentEdges = await CodeNodeNavigationQueries.ProjectDependencyEdges(
            context,
            context.DependencyEdges
                    .AsNoTracking()
                .Where(edge => edge.EdgeType == EdgeType.DocumentLink)
                .OrderBy(edge => edge.CallerNodeId)
                .ThenBy(edge => edge.CalleeNodeId))
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var directories = await context.Directories
            .AsNoTracking()
            .Select(static directory => directory.Path)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var documents = await context.Documents
            .AsNoTracking()
            .Select(static document => document.RelativePath)
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
        directories.Should().Contain("docs");
        documents.Should().Contain("docs/Guide.md");
        documents.Should().Contain("docs/Reference.md");
    }

    [Fact]
    public async Task WhenReindexingUnchangedTarget_ThenPreservesPersistedIntegerIds()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory(),
            fullMarkdownNodes: CreateGuideAndReferenceDocuments(),
            incrementalMarkdownNodes: EmptyNodes(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);
        var persistedIds = await context.CodeNodes
            .AsNoTracking()
            .Join(
            context.GraphNodes.AsNoTracking(),
            codeNode => codeNode.Id,
            graphNode => graphNode.Id,
            (codeNode, graphNode) => new
            {
                codeNode.Id,
                graphNode.CanonicalId
            })
            .OrderBy(candidate => candidate.CanonicalId)
            .ToDictionaryAsync(
            candidate => candidate.CanonicalId,
            candidate => candidate.Id,
            TestContext.Current.CancellationToken);

        await indexing.Index(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var reindexedIds = await context.CodeNodes
            .AsNoTracking()
            .Join(
            context.GraphNodes.AsNoTracking(),
            codeNode => codeNode.Id,
            graphNode => graphNode.Id,
            (codeNode, graphNode) => new
            {
                codeNode.Id,
                graphNode.CanonicalId
            })
            .OrderBy(candidate => candidate.CanonicalId)
            .ToDictionaryAsync(
            candidate => candidate.CanonicalId,
            candidate => candidate.Id,
            TestContext.Current.CancellationToken);

        reindexedIds.Should().BeEquivalentTo(persistedIds);
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForModifiedMarkdown_ThenReplacesDocumentNodesAndSearchRows()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory(),
            fullMarkdownNodes: CreateGettingStartedDocuments(),
            incrementalMarkdownNodes: CreateIncrementalGuideDocuments(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);

        await indexing.UpdateIncremental(
            new UpdateWorkspaceFilesCommand(
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: "/repo/docs/Guide.md")
                ]),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var guideNodesQuery =
            from codeNode in context.CodeNodes.AsNoTracking()
            join document in context.Documents.AsNoTracking() on codeNode.DocumentId equals document.Id
            where document.RelativePath == "docs/Guide.md"
            select codeNode;
        var guideNodes = await CodeNodeNavigationQueries.ProjectCodeNodes(context, guideNodesQuery)
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
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory(),
            fullMarkdownNodes: CreateLinkedDocuments(),
            incrementalMarkdownNodes: CreateUpdatedLinkedDocuments() with
            {
                CodeNodes = [.. CreateLinkedDocuments().CodeNodes.Where(node => node.RelativeFilePath.EndsWith("DocA.md", StringComparison.Ordinal)), .. CreateUpdatedLinkedDocuments().CodeNodes],
                Edges = CreateLinkedDocuments().Edges
            },
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);

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
            .Join(
            context.GraphNodes.AsNoTracking(),
            edge => edge.CallerNodeId,
            graphNode => graphNode.Id,
            (edge, callerNode) => new
            {
                edge,
                callerNode
            })
            .Join(
            context.GraphNodes.AsNoTracking(),
            candidate => candidate.edge.CalleeNodeId,
            graphNode => graphNode.Id,
            (candidate, calleeNode) => new
            {
                candidate.edge,
                candidate.callerNode,
                calleeNode
            })
            .CountAsync(
            candidate =>
                    candidate.callerNode.CanonicalId == "code:doc:docs/DocA.md#document-root" &&
                    candidate.calleeNode.CanonicalId == "code:doc:docs/DocB.md#document-root" &&
                    candidate.edge.EdgeType == EdgeType.DocumentLink,
            TestContext.Current.CancellationToken);

        inboundEdgeCount.Should().Be(1);
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForDeletedMarkdown_ThenRemovesDocumentNodesAndSearchRows()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory(),
            fullMarkdownNodes: CreateLinkedDocuments(),
            incrementalMarkdownNodes: CreateLinkedDocuments() with
            {
                CodeNodes = [.. CreateLinkedDocuments().CodeNodes.Where(node => node.RelativeFilePath.EndsWith("DocA.md", StringComparison.Ordinal))],
                Edges = []
            },
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);

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
            .Join(
            context.Documents.AsNoTracking(),
            codeNode => codeNode.DocumentId,
            document => document.Id,
            (codeNode, document) => new
            {
                codeNode,
                document
            })
            .CountAsync(
            candidate => candidate.document.RelativePath == "docs/DocB.md",
            TestContext.Current.CancellationToken);
        var searchCount = await context.Database
            .SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM CodeNodeSearch WHERE CanonicalId = {0}",
            "code:doc:docs/DocB.md#document-root")
            .SingleAsync(TestContext.Current.CancellationToken);

        deletedNodeCount.Should().Be(0);
        searchCount.Should().Be(0);
        context.Documents.Should().NotContain(document => document.RelativePath == "docs/DocB.md");
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForDeletedDirectory_ThenRemovesContainedDocumentNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory(),
            fullMarkdownNodes: CreateNestedDirectoryDocuments(),
            incrementalMarkdownNodes: EmptyNodes(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);

        await indexing.UpdateIncremental(
            new UpdateWorkspaceFilesCommand(
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.DirectoryDeleted,
                        OldPath: "/repo/docs/Legacy")
                ]),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var deletedNodeCount = await context.CodeNodes
            .AsNoTracking()
            .Join(
            context.Documents.AsNoTracking(),
            codeNode => codeNode.DocumentId,
            document => document.Id,
            (codeNode, document) => new
            {
                codeNode,
                document
            })
            .CountAsync(
            candidate => candidate.document.RelativePath.StartsWith("docs/Legacy/"),
            TestContext.Current.CancellationToken);
        var documentPaths = await context.Documents
            .AsNoTracking()
            .Select(static document => document.RelativePath)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        deletedNodeCount.Should().Be(0);
        documentPaths.Should().NotContain(path => path.StartsWith("docs/Legacy/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForRenamedDirectory_ThenReplacesContainedDocumentPaths()
    {
        var expectedDiscoveryGlobs = new[]
        {
            "**/*.cs",
            "**/*.ts",
            "**/*.tsx",
            "**/*.md",
            "**/*.markdown",
            "**/*.mdown",
            "**/*.mkd"
        };
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(
            options => new SharpSenseDbContext(options),
            new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext(
            ct: TestContext.Current.CancellationToken);
        var indexing = CreateIndexing(
            inMemoryFactory.CreateDbContextFactory(),
            fullMarkdownNodes: CreateLegacyGuideDocuments(),
            incrementalMarkdownNodes: CreateCurrentGuideDocuments(),
            workspacePaths: CreateWorkspacePaths());

        await indexing.Index(new IndexWorkspaceCommand(), TestContext.Current.CancellationToken);

        await indexing.UpdateIncremental(
            new UpdateWorkspaceFilesCommand(
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.DirectoryRenamed,
                        OldPath: "/repo/docs/Legacy",
                        NewPath: "/repo/docs/Current")
                ]),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        var documentPaths = await context.Documents
            .AsNoTracking()
            .Select(static document => document.RelativePath)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        documentPaths.Should().Contain("docs/Current/Guide.md");
        documentPaths.Should().NotContain("docs/Legacy/Guide.md");
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
                    "See [Reference](./Reference.md).",
                    "Guide\nSee [Reference](./Reference.md)."),
                new IndexedCodeNode(
                    "code:doc:docs/Reference.md#document-root",
                    null,
                    "docs/Reference.md#document-root",
                    "Reference",
                    NodeType.Document,
                    "docs/Reference.md",
                    1,
                    1,
                    "Reference content.",
                    "Reference\nReference content.")
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
                    "Getting started guide.",
                    "Getting Started\nGetting started guide.")
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
                    "Watch mode should refresh this guide.",
                    "Incremental Guide\nWatch mode should refresh this guide.")
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
                    "See [DocB](./DocB.md).",
                    "DocA\nSee [DocB](./DocB.md)."),
                new IndexedCodeNode(
                    "code:doc:docs/DocB.md#document-root",
                    null,
                    "docs/DocB.md#document-root",
                    "DocB",
                    NodeType.Document,
                    "docs/DocB.md",
                    1,
                    1,
                    "Doc B reference content.",
                    "DocB\nDoc B reference content.")
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
                    "Doc B reference content updated.",
                    "DocB\nDoc B reference content updated.")
            ],
            [],
            []);

    private static ExtractedNodes CreateNestedDirectoryDocuments()
        => new(
            [],
            [
                new IndexedCodeNode(
                    "code:doc:docs/Legacy/Guide.md#document-root",
                    null,
                    "docs/Legacy/Guide.md#document-root",
                    "Guide",
                    NodeType.Document,
                    "docs/Legacy/Guide.md",
                    1,
                    1,
                    "Legacy guide.",
                    "Guide\nLegacy guide.")
            ],
            [],
            []);

    private static ExtractedNodes CreateLegacyGuideDocuments()
        => new(
            [],
            [
                new IndexedCodeNode(
                    "code:doc:docs/Legacy/Guide.md#document-root",
                    null,
                    "docs/Legacy/Guide.md#document-root",
                    "Guide",
                    NodeType.Document,
                    "docs/Legacy/Guide.md",
                    1,
                    1,
                    "Legacy guide.",
                    "Guide\nLegacy guide.")
            ],
            [],
            []);

    private static ExtractedNodes CreateCurrentGuideDocuments()
        => new(
            [],
            [
                new IndexedCodeNode(
                    "code:doc:docs/Current/Guide.md#document-root",
                    null,
                    "docs/Current/Guide.md#document-root",
                    "Guide",
                    NodeType.Document,
                    "docs/Current/Guide.md",
                    1,
                    1,
                    "Current guide.",
                    "Guide\nCurrent guide.")
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
        workspacePaths.Setup(candidate => candidate.GetRequiredTargetPath("docs"))
            .Returns("/repo/docs");
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
        markdownExtractor.SetupGet(candidate => candidate.SourceKind)
            .Returns(WorkspaceSourceKind.Markdown);
        markdownExtractor.Setup(candidate => candidate.Extract(It.IsAny<ExtractionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExtractionContext context, CancellationToken _) =>
                context.ChangedFiles is null ? fullMarkdownNodes : incrementalMarkdownNodes);

        return new KnowledgeGraphIndexing(
            [markdownExtractor.Object],
            new NoOpEmbeddingGenerator(),
            new KnowledgeGraphRepository(dbContextFactory),
            workspacePaths.Object,
            Options.Create(new WorkspaceExecutionOptions
            {
                RepositoryRoot = "/repo",
                WorkspaceSources = [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")],
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
        IOptions<WorkspaceExecutionOptions> options)
    {
        private readonly IndexWorkspaceCommandHandler _indexTargetHandler = new IndexWorkspaceCommandHandler(
            embeddingGenerator,
            knowledgeGraphRepository,
            workspacePaths,
            options,
            new WorkspaceExtractionCoordinator(
                extractors,
                workspacePaths,
                Moq.Mock.Of<IWorkspaceChangeFilter>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance),
            Mock.Of<SharpSense.Application.GraphStats.Abstractions.IIndexRunStore>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance);

        private readonly UpdateWorkspaceFilesCommandHandler _updateWorkspaceFilesHandler = new UpdateWorkspaceFilesCommandHandler(
            new IndexWorkspaceCommandHandler(
                embeddingGenerator,
                knowledgeGraphRepository,
                workspacePaths,
                options,
                new WorkspaceExtractionCoordinator(
                    extractors,
                    workspacePaths,
                    Moq.Mock.Of<IWorkspaceChangeFilter>(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance),
                Mock.Of<SharpSense.Application.GraphStats.Abstractions.IIndexRunStore>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance),
            Mock.Of<IWorkspaceChangeFilter>(filter => filter.IsRelevant(It.IsAny<IReadOnlyList<WorkspaceFileChange>>()) == true));

        public Task Index(
            IndexWorkspaceCommand command,
            CancellationToken ct)
            => _indexTargetHandler.Handle(command, ct);

        public Task UpdateIncremental(
            UpdateWorkspaceFilesCommand command,
            CancellationToken ct)
            => _updateWorkspaceFilesHandler.Handle(command, ct);
    }
}
