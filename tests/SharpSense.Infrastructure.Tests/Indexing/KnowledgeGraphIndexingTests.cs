using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Features.Indexing.IndexTarget;
using SharpSense.Application.Features.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Indexing;

[Collection("MSBuild workspace")]
public sealed class KnowledgeGraphIndexingTests
{
    [Fact]
    public async Task WhenIndexingTargetWithDocumentConfig_ThenPersistsDiscoveredDocumentNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var repositoryRoot = GetRepositoryRoot();
        var targetPath = GetFixturePath("CommandPipelineFixture.sln");
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
        var indexing = CreateIndexing(context, workspace, targetPath);

        await indexing.Index(
            new IndexTargetCommand(),
            TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var documentNodes = await context.CodeNodes
            .AsNoTracking()
            .Where((CodeNode codeNode) => codeNode.NodeType == NodeType.Document)
            .OrderBy(codeNode => codeNode.CanonicalId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var documentEdges = await context.DependencyEdges
            .AsNoTracking()
            .Where(edge => edge.EdgeType == EdgeType.DocumentLink)
            .OrderBy(edge => edge.CallerId)
            .ThenBy(edge => edge.CalleeId)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        var hierarchyEdges = await context.DependencyEdges
            .AsNoTracking()
            .Where(edge => edge.EdgeType == EdgeType.DocumentHierarchy)
            .OrderBy(edge => edge.CallerId)
            .ThenBy(edge => edge.CalleeId)
            .ToArrayAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, documentNodes.Length);
        Assert.Collection(
            documentNodes,
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocA.md#document-root", node.CanonicalId);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocA.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Contains("[DocB](./DocB.md)", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocB.md#document-root", node.CanonicalId);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocB.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Contains("Doc B reference content.", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#document-root", node.CanonicalId);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(1, node.EndLine);
                Assert.Equal(string.Empty, node.Summary);
            },
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#getting-started", node.CanonicalId);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md", node.RelativeFilePath);
                Assert.Equal(1, node.StartLine);
                Assert.Equal(2, node.EndLine);
                Assert.Contains("pipeline fixture documentation", node.Summary, StringComparison.Ordinal);
            },
            node =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#getting-started-l4", node.CanonicalId);
                Assert.Null(node.ProjectId);
                Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md", node.RelativeFilePath);
                Assert.Equal(4, node.StartLine);
                Assert.Equal(5, node.EndLine);
                Assert.Contains("repeated heading", node.Summary, StringComparison.Ordinal);
            });
        Assert.Collection(
            documentEdges,
            edge =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocA.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/DocB.md#document-root", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentLink, edge.EdgeType);
            });
        Assert.Collection(
            hierarchyEdges,
            edge =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#getting-started", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            },
            edge =>
            {
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#document-root", edge.CallerId);
                Assert.Equal("code:doc:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/docs/Guide.md#getting-started-l4", edge.CalleeId);
                Assert.Equal(EdgeType.DocumentHierarchy, edge.EdgeType);
            });
        Assert.True(documentNodes.Length > 0);
    }

    [Fact]
    public async Task WhenIndexingWithRelativeTargetPath_ThenResolvesAgainstConfiguredRepositoryRoot()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var repositoryRoot = GetRepositoryRoot();
        var absoluteTargetPath = GetFixturePath("CommandPipelineFixture.sln");
        var relativeTargetPath = Path.GetRelativePath(repositoryRoot, absoluteTargetPath);
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
        var indexing = CreateIndexing(context, workspace, relativeTargetPath);

        await indexing.Index(
            new IndexTargetCommand(),
            TestContext.Current.CancellationToken);
        var persistedNodeCount = await context.CodeNodes.CountAsync(TestContext.Current.CancellationToken);

        Assert.True(persistedNodeCount > 0);
    }

    [Fact]
    public async Task WhenReindexingUnchangedTarget_ThenPreservesPersistedIntegerIds()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var fixtureRoot = CreateMutableFixtureWorkspace();
        var solutionPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.sln");

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(fixtureRoot);
            var indexing = CreateIndexing(context, workspace, solutionPath);

            await indexing.Index(
                new IndexTargetCommand(),
                TestContext.Current.CancellationToken);
            var persistedIds = await context.CodeNodes
                .AsNoTracking()
                .OrderBy(codeNode => codeNode.CanonicalId)
                .ToDictionaryAsync(
                    codeNode => codeNode.CanonicalId,
                    codeNode => codeNode.Id,
                    TestContext.Current.CancellationToken);

            await indexing.Index(
                new IndexTargetCommand(),
                TestContext.Current.CancellationToken);
            context.ChangeTracker.Clear();

            var reindexedIds = await context.CodeNodes
                .AsNoTracking()
                .OrderBy(codeNode => codeNode.CanonicalId)
                .ToDictionaryAsync(
                    codeNode => codeNode.CanonicalId,
                    codeNode => codeNode.Id,
                    TestContext.Current.CancellationToken);

            Assert.Equal(persistedIds.Count, reindexedIds.Count);

            foreach (var persistedId in persistedIds)
            {
                Assert.True(reindexedIds.TryGetValue(persistedId.Key, out var reindexedId));
                Assert.Equal(persistedId.Value, reindexedId);
            }
        }
        finally
        {
            DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [Fact]
    public async Task WhenIndexingTargetWithWikiDocuments_ThenPersistsWikiDocumentNodesAndLinks()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var fixtureRoot = CreateMutableFixtureWorkspace();
        var solutionPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.sln");
        var wikiRoot = Path.Combine(fixtureRoot, "docs", "wiki");

        try
        {
            WriteTextFile(
                Path.Combine(wikiRoot, "index.md"),
                """
                # Wiki Home
                See [[extractors/markdown]], [[persistence/sqlite-schema#target-overwrites]], and [[persistence/sqlite-schema|schema]].
                """);
            WriteTextFile(
                Path.Combine(wikiRoot, "extractors", "markdown.md"),
                """
                # Markdown
                ## Incremental Logic
                Extractor details.
                """);
            WriteTextFile(
                Path.Combine(wikiRoot, "persistence", "sqlite-schema.md"),
                """
                # SQLite Schema
                ## Target Overwrites
                Persistence details.
                """);
            WriteTextFile(
                Path.Combine(wikiRoot, "architecture", "incremental-watch.md"),
                """
                # Incremental Watch
                See [[extractors/markdown]], [[persistence/sqlite-schema#target-overwrites|schema]], and [[#local-notes]].

                ## Local Notes
                Watch details.
                """);

            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(fixtureRoot);
            var indexing = CreateIndexing(context, workspace, solutionPath);

            await indexing.Index(
                new IndexTargetCommand(),
                TestContext.Current.CancellationToken);
            context.ChangeTracker.Clear();

            var wikiNodes = await context.CodeNodes
                .AsNoTracking()
                .Where(codeNode => codeNode.RelativeFilePath.StartsWith("docs/wiki/"))
                .OrderBy(codeNode => codeNode.CanonicalId)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            var wikiDocumentLinks = await context.DependencyEdges
                .AsNoTracking()
                .Where(edge =>
                    edge.EdgeType == EdgeType.DocumentLink &&
                    edge.CallerId.StartsWith("code:doc:docs/wiki/"))
                .OrderBy(edge => edge.CallerId)
                .ThenBy(edge => edge.CalleeId)
                .ToArrayAsync(TestContext.Current.CancellationToken);
            var wikiHierarchyEdges = await context.DependencyEdges
                .AsNoTracking()
                .Where(edge =>
                    edge.EdgeType == EdgeType.DocumentHierarchy &&
                    edge.CallerId.StartsWith("code:doc:docs/wiki/"))
                .OrderBy(edge => edge.CallerId)
                .ThenBy(edge => edge.CalleeId)
                .ToArrayAsync(TestContext.Current.CancellationToken);

            Assert.Equal(11, wikiNodes.Length);
            Assert.Contains(
                wikiNodes,
                static node =>
                    node.CanonicalId == "code:doc:docs/wiki/index.md#document-root" &&
                    node.DisplayName == "index");
            Assert.Contains(
                wikiNodes,
                static node =>
                    node.CanonicalId == "code:doc:docs/wiki/extractors/markdown.md#document-root" &&
                    node.DisplayName == "extractors/markdown");
            Assert.Contains(
                wikiNodes,
                static node =>
                    node.CanonicalId == "code:doc:docs/wiki/persistence/sqlite-schema.md#target-overwrites" &&
                    node.DisplayName == "persistence/sqlite-schema#target-overwrites");
            Assert.Contains(
                wikiNodes,
                static node =>
                    node.CanonicalId == "code:doc:docs/wiki/architecture/incremental-watch.md#local-notes" &&
                    node.DisplayName == "architecture/incremental-watch#local-notes");

            Assert.Contains(
                wikiDocumentLinks,
                static edge =>
                    edge.CallerId == "code:doc:docs/wiki/index.md#wiki-home" &&
                    edge.CalleeId == "code:doc:docs/wiki/extractors/markdown.md#document-root" &&
                    edge.EdgeType == EdgeType.DocumentLink);
            Assert.Contains(
                wikiDocumentLinks,
                static edge =>
                    edge.CallerId == "code:doc:docs/wiki/index.md#wiki-home" &&
                    edge.CalleeId == "code:doc:docs/wiki/persistence/sqlite-schema.md#target-overwrites" &&
                    edge.EdgeType == EdgeType.DocumentLink);
            Assert.Contains(
                wikiDocumentLinks,
                static edge =>
                    edge.CallerId == "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch" &&
                    edge.CalleeId == "code:doc:docs/wiki/extractors/markdown.md#document-root" &&
                    edge.EdgeType == EdgeType.DocumentLink);
            Assert.Contains(
                wikiDocumentLinks,
                static edge =>
                    edge.CallerId == "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch" &&
                    edge.CalleeId == "code:doc:docs/wiki/architecture/incremental-watch.md#local-notes" &&
                    edge.EdgeType == EdgeType.DocumentLink);

            Assert.Contains(
                wikiHierarchyEdges,
                static edge =>
                    edge.CallerId == "code:doc:docs/wiki/architecture/incremental-watch.md#document-root" &&
                    edge.CalleeId == "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch" &&
                    edge.EdgeType == EdgeType.DocumentHierarchy);
            Assert.Contains(
                wikiHierarchyEdges,
                static edge =>
                    edge.CallerId == "code:doc:docs/wiki/architecture/incremental-watch.md#incremental-watch" &&
                    edge.CalleeId == "code:doc:docs/wiki/architecture/incremental-watch.md#local-notes" &&
                    edge.EdgeType == EdgeType.DocumentHierarchy);
        }
        finally
        {
            DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForModifiedMarkdown_ThenReplacesDocumentNodesAndSearchRows()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var fixtureRoot = CreateMutableFixtureWorkspace();
        var solutionPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.sln");
        var guidePath = Path.Combine(fixtureRoot, "docs", "Guide.md");

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(fixtureRoot);
            var indexing = CreateIndexing(context, workspace, solutionPath);

            await indexing.Index(
                new IndexTargetCommand(),
                TestContext.Current.CancellationToken);
            File.WriteAllText(
                guidePath,
                """
                # Incremental Guide
                Watch mode should refresh this guide.
                """);

            await indexing.UpdateIncremental(
                new UpdateWorkspaceFilesCommand(
                    [
                        new WorkspaceFileChange(
                            WorkspaceFileChangeAction.Modified,
                            NewPath: guidePath)
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

            Assert.Contains(guideNodes, static node => node.CanonicalId == "code:doc:docs/Guide.md#incremental-guide");
            Assert.DoesNotContain(guideNodes, static node => node.CanonicalId == "code:doc:docs/Guide.md#getting-started");
            Assert.Equal(0, oldSearchCount);
            Assert.Equal(1, newSearchCount);
        }
        finally
        {
            DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForModifiedMarkdown_ThenPreservesInboundEdgesFromUnchangedDocuments()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var fixtureRoot = CreateMutableFixtureWorkspace();
        var solutionPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.sln");
        var modifiedDocumentPath = Path.Combine(fixtureRoot, "docs", "DocB.md");

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(fixtureRoot);
            var indexing = CreateIndexing(context, workspace, solutionPath);

            await indexing.Index(
                new IndexTargetCommand(),
                TestContext.Current.CancellationToken);
            File.WriteAllText(
                modifiedDocumentPath,
                """
                Doc B reference content updated.
                """);

            await indexing.UpdateIncremental(
                new UpdateWorkspaceFilesCommand(
                    [
                        new WorkspaceFileChange(
                            WorkspaceFileChangeAction.Modified,
                            NewPath: modifiedDocumentPath)
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

            Assert.Equal(1, inboundEdgeCount);
        }
        finally
        {
            DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [Fact]
    public async Task WhenUpdatingWorkspaceFilesForDeletedMarkdown_ThenRemovesDocumentNodesAndSearchRows()
    {
        await using var inMemoryFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
            UseMigrations: true,
            LoadVectorExtension: true));
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(
            ct: TestContext.Current.CancellationToken);
        var fixtureRoot = CreateMutableFixtureWorkspace();
        var solutionPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.sln");
        var deletedDocumentPath = Path.Combine(fixtureRoot, "docs", "DocB.md");

        try
        {
            var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(fixtureRoot);
            var indexing = CreateIndexing(context, workspace, solutionPath);

            await indexing.Index(
                new IndexTargetCommand(),
                TestContext.Current.CancellationToken);
            var inboundEdgeCountBeforeDelete = await context.DependencyEdges
                .AsNoTracking()
                .CountAsync(
                    edge =>
                        edge.CallerId == "code:doc:docs/DocA.md#document-root" &&
                        edge.CalleeId == "code:doc:docs/DocB.md#document-root" &&
                        edge.EdgeType == EdgeType.DocumentLink,
                    TestContext.Current.CancellationToken);
            File.Delete(deletedDocumentPath);

            await indexing.UpdateIncremental(
                new UpdateWorkspaceFilesCommand(
                    [
                        new WorkspaceFileChange(
                            WorkspaceFileChangeAction.Deleted,
                            OldPath: deletedDocumentPath)
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
            var inboundEdgeCountAfterDelete = await context.DependencyEdges
                .AsNoTracking()
                .CountAsync(
                    edge =>
                        edge.CallerId == "code:doc:docs/DocA.md#document-root" &&
                        edge.CalleeId == "code:doc:docs/DocB.md#document-root" &&
                        edge.EdgeType == EdgeType.DocumentLink,
                    TestContext.Current.CancellationToken);

            Assert.Equal(1, inboundEdgeCountBeforeDelete);
            Assert.Equal(0, deletedNodeCount);
            Assert.Equal(0, searchCount);
            Assert.Equal(0, inboundEdgeCountAfterDelete);
        }
        finally
        {
            DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    private static string GetFixturePath(string relativePath)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture",
            relativePath));

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private static string CreateMutableFixtureWorkspace()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"sharp-sense-indexing-{Guid.NewGuid():N}");
        CopyDirectory(GetFixturePath(string.Empty), fixtureRoot);
        Directory.CreateDirectory(Path.Combine(fixtureRoot, ".git"));
        return fixtureRoot;
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relativeDirectory));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativeFile = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile);
        }
    }

    private static void WriteTextFile(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static KnowledgeGraphIndexing CreateIndexing(
        SharpSenseDbContext context,
        IRepositoryWorkspace workspace,
        string targetPath)
        => new(
            new ILanguageExtractor[]
            {
                new CSharpLanguageExtractor(new RoslynTargetAnalysisEngine(), workspace),
                new MarkdownDocumentExtractor(
                    new DocumentDiscoverer(
                        workspace,
                        CreateConfigMonitor("docs/**/*.md"),
                        new WorkspaceFileDiscoverer(workspace),
                        new MarkdownIndexer()))
            },
            new NoOpEmbeddingGenerator(),
            context,
            workspace,
            Options.Create(new SharpSenseCliOptions
            {
                RepositoryRoot = workspace.RootPath,
                TargetPath = targetPath,
                SkipEmbeddings = true
            }));

    private static IOptionsMonitor<SharpSenseConfig> CreateConfigMonitor(params string[] includePaths)
    {
        var configMonitor = new Mock<IOptionsMonitor<SharpSenseConfig>>(MockBehavior.Strict);
        configMonitor.SetupGet(monitor => monitor.CurrentValue)
            .Returns(new SharpSenseConfig
            {
                IncludePaths = includePaths
            });
        return configMonitor.Object;
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
}
