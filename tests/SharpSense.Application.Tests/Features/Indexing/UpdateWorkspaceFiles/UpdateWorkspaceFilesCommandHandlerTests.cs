using FluentResults;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Features.Indexing.UpdateWorkspaceFiles;

public sealed class UpdateWorkspaceFilesCommandHandlerTests
{
    [Fact]
    public async Task WhenHandleWithValidCommand_ThenPersistsNormalizedIncrementalGraph()
    {
        var expectedEmbedding = new[] { 1f, 2f };
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/repo/src/SharpSense.Infrastructure/Indexing/KnowledgeGraphRepository.cs")
            ]);
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("csharp");
        extractor.Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                CancellationToken.None))
            .ReturnsAsync(new ExtractedNodes(
                [],
                [
                    new IndexedCodeNode(
                        "code:project-app:App.Feature.Run()",
                        "project-app",
                        "App.Feature.Run()",
                        "Feature.Run()",
                        NodeType.Method,
                        "/repo/src/App/Feature.cs",
                        10,
                        20,
                        "Runs the feature.",
                        expectedSearchText)
                ],
                [],
                []));
        var embeddingGenerator = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddingGenerator.Setup(candidate => candidate.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { expectedSearchText })),
                null,
                CancellationToken.None))
            .ReturnsAsync([new TextEmbedding(expectedSearchText, expectedEmbedding)]);
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetPersistedCodeNodes(
                CancellationToken.None))
            .ReturnsAsync([]);
        repository.Setup(candidate => candidate.ReplaceTarget(
                It.Is<ExtractedNodes>(payload =>
                    payload.CodeNodes.Single().RelativeFilePath == "src/App/Feature.cs" &&
                    payload.CodeNodes.Single().VectorEmbedding!.SequenceEqual(expectedEmbedding)),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var workspacePaths = CreateWorkspacePaths();
        var handler = CreateHandler(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            workspacePaths.Object,
            new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
            new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            });

        await handler.Handle(command, CancellationToken.None);

        embeddingGenerator.Verify(candidate => candidate.GenerateBatch(It.IsAny<IEnumerable<string>>(), null, CancellationToken.None), Times.Once);
        repository.Verify(candidate => candidate.GetPersistedCodeNodes(CancellationToken.None), Times.Once);
        repository.Verify(candidate => candidate.ReplaceTarget(It.IsAny<ExtractedNodes>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WhenHandleWithRenamedDirectory_ThenExpandsDirectoryChangeBeforePersisting()
    {
        var expectedDiscoveryGlobs = new[] { "**/*.cs", "**/*.ts", "**/*.tsx", "**/*.md", "**/*.markdown", "**/*.mdown", "**/*.mkd" };
        const string expectedSearchText = "Guide\nGuide content.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.DirectoryRenamed,
                    OldPath: "/repo/docs/Old",
                    NewPath: "/repo/docs/New")
            ]);
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("markdown");
        extractor.Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                CancellationToken.None))
            .ReturnsAsync(new ExtractedNodes(
                [],
                [
                    new IndexedCodeNode(
                        "code:doc:docs/New/Guide.md#document-root",
                        null,
                        "docs/New/Guide.md#document-root",
                        "Guide",
                        NodeType.Document,
                        "/repo/docs/New/Guide.md",
                        1,
                        1,
                        "Guide content.",
                        expectedSearchText)
                ],
                [],
                []));
        var embeddingGenerator = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddingGenerator.Setup(candidate => candidate.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { expectedSearchText })),
                null,
                CancellationToken.None))
            .ReturnsAsync([new TextEmbedding(expectedSearchText, [1f])]);
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetPersistedDocumentPathsUnderDirectory("docs/Old", CancellationToken.None))
            .ReturnsAsync(["docs/Old/Guide.md"]);
        repository.Setup(candidate => candidate.GetPersistedCodeNodes(
                CancellationToken.None))
            .ReturnsAsync([]);
        repository.Setup(candidate => candidate.ReplaceTarget(
                It.Is<ExtractedNodes>(payload => payload.CodeNodes.Single().RelativeFilePath == "docs/New/Guide.md"),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var workspacePaths = CreateWorkspacePaths();
        var workspaceFileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        workspaceFileDiscoverer.Setup(candidate => candidate.GetAllowedFiles(
                "/repo/docs/New",
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(expectedDiscoveryGlobs)),
                CancellationToken.None))
            .ReturnsAsync(
            [
                new DiscoveredFile(
                    "/repo/docs/New/Guide.md",
                    "docs/New/Guide.md"),
                new DiscoveredFile(
                    "/repo/docs/New/node_modules/Generated.md",
                    "docs/New/node_modules/Generated.md")
            ]);
        var handler = CreateHandler(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            workspacePaths.Object,
            workspaceFileDiscoverer.Object,
            new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            });

        await handler.Handle(command, CancellationToken.None);

        repository.VerifyAll();
        workspaceFileDiscoverer.VerifyAll();
    }

    [Fact]
    public async Task WhenHandleWithMatchingPersistedFingerprint_ThenReusesStoredVector()
    {
        var expectedEmbedding = new[] { 4f, 2f };
        const string bodyHash = "hash-1";
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/repo/src/App/Feature.cs")
            ]);
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("csharp");
        extractor.Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                CancellationToken.None))
            .ReturnsAsync(new ExtractedNodes(
                [],
                [
                    new IndexedCodeNode(
                        "code:project-app:App.Feature.Run()",
                        "project-app",
                        "App.Feature.Run()",
                        "Feature.Run()",
                        NodeType.Method,
                        "/repo/src/App/Feature.cs",
                        10,
                        20,
                        "Runs the feature.",
                        expectedSearchText,
                        bodyHash)
                ],
                [],
                []));
        var embeddingGenerator = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetPersistedCodeNodes(
                CancellationToken.None))
            .ReturnsAsync(
            [
                new IndexedCodeNode(
                    "code:project-app:App.Feature.Run()",
                    "project-app",
                    "App.Feature.Run()",
                    "Feature.Run()",
                    NodeType.Method,
                    "src/App/Feature.cs",
                    10,
                    20,
                    "Runs the feature.",
                    expectedSearchText,
                    bodyHash,
                    expectedEmbedding)
            ]);
        repository.Setup(candidate => candidate.ReplaceTarget(
                It.Is<ExtractedNodes>(payload => payload.CodeNodes.Single().VectorEmbedding!.SequenceEqual(expectedEmbedding)),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            CreateWorkspacePaths().Object,
            new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
            new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            });

        await handler.Handle(command, CancellationToken.None);

        embeddingGenerator.Verify(candidate => candidate.GenerateBatch(It.IsAny<IEnumerable<string>>(), null, CancellationToken.None), Times.Never);
        repository.Verify(candidate => candidate.GetPersistedCodeNodes(CancellationToken.None), Times.Once);
        repository.Verify(candidate => candidate.ReplaceTarget(It.IsAny<ExtractedNodes>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WhenHandleWithNoCacheAndMatchingPersistedFingerprint_ThenRegeneratesEmbedding()
    {
        var expectedEmbedding = new[] { 5f, 1f };
        const string bodyHash = "hash-1";
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/repo/src/App/Feature.cs")
            ]);
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("csharp");
        extractor.Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                CancellationToken.None))
            .ReturnsAsync(new ExtractedNodes(
                [],
                [
                    new IndexedCodeNode(
                        "code:project-app:App.Feature.Run()",
                        "project-app",
                        "App.Feature.Run()",
                        "Feature.Run()",
                        NodeType.Method,
                        "/repo/src/App/Feature.cs",
                        10,
                        20,
                        "Runs the feature.",
                        expectedSearchText,
                        bodyHash)
                ],
                [],
                []));
        var embeddingGenerator = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddingGenerator.Setup(candidate => candidate.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { expectedSearchText })),
                null,
                CancellationToken.None))
            .ReturnsAsync([new TextEmbedding(expectedSearchText, expectedEmbedding)]);
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.ReplaceTarget(
                It.Is<ExtractedNodes>(payload => payload.CodeNodes.Single().VectorEmbedding!.SequenceEqual(expectedEmbedding)),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            CreateWorkspacePaths().Object,
            new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
            new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo",
                DisableEmbeddingCache = true
            });

        await handler.Handle(command, CancellationToken.None);

        embeddingGenerator.Verify(candidate => candidate.GenerateBatch(It.IsAny<IEnumerable<string>>(), null, CancellationToken.None), Times.Once);
        repository.Verify(candidate => candidate.GetPersistedCodeNodes(CancellationToken.None), Times.Never);
        repository.Verify(candidate => candidate.ReplaceTarget(It.IsAny<ExtractedNodes>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WhenHandleWithChangedSearchTextButMatchingBodyHash_ThenRegeneratesEmbedding()
    {
        var expectedEmbedding = new[] { 6f, 3f };
        const string bodyHash = "hash-1";
        const string persistedSearchText = "Feature.Run()\nRuns the feature.";
        const string updatedSearchText = "Feature.Run()\nExplains the updated behavior.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/repo/src/App/Feature.cs")
            ]);
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("csharp");
        extractor.Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                CancellationToken.None))
            .ReturnsAsync(new ExtractedNodes(
                [],
                [
                    new IndexedCodeNode(
                        "code:project-app:App.Feature.Run()",
                        "project-app",
                        "App.Feature.Run()",
                        "Feature.Run()",
                        NodeType.Method,
                        "/repo/src/App/Feature.cs",
                        10,
                        20,
                        "Explains the updated behavior.",
                        updatedSearchText,
                        bodyHash)
                ],
                [],
                []));
        var embeddingGenerator = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddingGenerator.Setup(candidate => candidate.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { updatedSearchText })),
                null,
                CancellationToken.None))
            .ReturnsAsync([new TextEmbedding(updatedSearchText, expectedEmbedding)]);
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetPersistedCodeNodes(
                CancellationToken.None))
            .ReturnsAsync(
            [
                new IndexedCodeNode(
                    "code:project-app:App.Feature.Run()",
                    "project-app",
                    "App.Feature.Run()",
                    "Feature.Run()",
                    NodeType.Method,
                    "src/App/Feature.cs",
                    10,
                    20,
                    "Runs the feature.",
                    persistedSearchText,
                    bodyHash,
                    [1f])
            ]);
        repository.Setup(candidate => candidate.ReplaceTarget(
                It.Is<ExtractedNodes>(payload => payload.CodeNodes.Single().VectorEmbedding!.SequenceEqual(expectedEmbedding)),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            CreateWorkspacePaths().Object,
            new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
            new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            });

        await handler.Handle(command, CancellationToken.None);

        embeddingGenerator.Verify(candidate => candidate.GenerateBatch(It.IsAny<IEnumerable<string>>(), null, CancellationToken.None), Times.Once);
        repository.Verify(candidate => candidate.ReplaceTarget(It.IsAny<ExtractedNodes>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WhenExtractorExpandsPartialSiblingsAndEmbeddingsAreSkipped_ThenRetainsSiblingVectors()
    {
        var sibling = new IndexedCodeNode("code:shared", "project:app", "Shared", "Shared", NodeType.Class,
            "src/App/PartA.cs", 1, 5, "Shared type", "Shared type", "same-body");
        var vector = new[] { 1f, 2f };
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName).Returns("csharp");
        extractor.Setup(candidate => candidate.Extract(It.IsAny<ExtractionContext>(), CancellationToken.None))
            .ReturnsAsync(new ExtractedNodes([], [sibling], [], []));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetPersistedCodeNodes(
                CancellationToken.None))
            .ReturnsAsync([sibling with { VectorEmbedding = vector }]);
        repository.Setup(candidate => candidate.ReplaceTarget(
                It.Is<ExtractedNodes>(nodes => nodes.CodeNodes.Single().VectorEmbedding!.SequenceEqual(vector)),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var handler = CreateHandler([extractor.Object], repository: repository.Object,
            workspacePaths: CreateWorkspacePaths().Object,
            options: new SharpSenseCliOptions { TargetPath = "SharpSense.sln", RepositoryRoot = "/repo", SkipEmbeddings = true });

        var result = await handler.Handle(new UpdateWorkspaceFilesCommand(
            [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/src/App/PartB.cs")]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repository.VerifyAll();
    }

    [Theory]
    [InlineData("src/deleted.ts", WorkspaceFileChangeAction.Deleted)]
    [InlineData("tsconfig.app.json", WorkspaceFileChangeAction.Modified)]
    [InlineData("package.json", WorkspaceFileChangeAction.Modified)]
    public async Task WhenTypeScriptScopeChanges_ThenReplacesPreviousScopeAndRefreshedConsumers(
        string changedPath, WorkspaceFileChangeAction action)
    {
        var survivor = new IndexedCodeNode("code:ts:src/consumer.ts:consumer", null, "consumer", "consumer",
            NodeType.Method, "/repo/src/consumer.ts", 1, 1, "", "consumer");
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName).Returns("TypeScript TreeSitter");
        extractor.Setup(candidate => candidate.ExtractIncremental(It.IsAny<IncrementalExtractionContext>(), CancellationToken.None))
            .ReturnsAsync(new ExtractedNodes([], [survivor], [], []));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetPersistedDocumentPathsUnderDirectory(string.Empty, CancellationToken.None))
            .ReturnsAsync(["src/consumer.ts", "src/deleted.ts", "shared/excluded.tsx", "Backend/Keep.cs", "docs/Keep.md"]);
        var expectedPaths = new[] { "shared/excluded.tsx", "src/consumer.ts", "src/deleted.ts" };
        repository.Setup(candidate => candidate.ReplaceWorkspaceFiles(
                It.Is<IReadOnlyList<string>>(paths => paths.SequenceEqual(expectedPaths)),
                It.Is<ExtractedNodes>(nodes => nodes.CodeNodes.Single().RelativeFilePath == "src/consumer.ts"),
                CancellationToken.None)).Returns(Task.CompletedTask);
        var handler = CreateHandler([extractor.Object], repository: repository.Object,
            workspacePaths: CreateWorkspacePaths().Object,
            options: new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln", RepositoryRoot = "/repo",
                SkipEmbeddings = true, DisableEmbeddingCache = true
            });
        var change = action == WorkspaceFileChangeAction.Deleted
            ? new WorkspaceFileChange(action, OldPath: "/repo/" + changedPath)
            : new WorkspaceFileChange(action, NewPath: "/repo/" + changedPath);

        var result = await handler.Handle(new UpdateWorkspaceFilesCommand([change]), CancellationToken.None);

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        repository.VerifyAll();
    }

    [Theory]
    [InlineData("Feature.cs")]
    [InlineData("App.csproj")]
    [InlineData("App.slnx")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Packages.props")]
    [InlineData("global.json")]
    [InlineData(".editorconfig")]
    public async Task WhenFullWorkspaceExtractionFails_ThenPreservesPreviouslyPersistedSnapshot(string changedPath)
    {
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName).Returns("csharp");
        extractor.Setup(candidate => candidate.Extract(It.IsAny<ExtractionContext>(), CancellationToken.None))
            .ReturnsAsync(Result.Fail<ExtractedNodes>("Cannot reload changed workspace"));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = CreateHandler([extractor.Object], repository: repository.Object,
            workspacePaths: CreateWorkspacePaths().Object);

        var result = await handler.Handle(new UpdateWorkspaceFilesCommand(
            [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/" + changedPath)]), CancellationToken.None);

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, error => error.Message == "Cannot reload changed workspace");
        repository.VerifyNoOtherCalls();
    }

    private static Mock<IIndexingWorkspacePaths> CreateWorkspacePaths()
    {
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths.Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths.Setup(candidate => candidate.TryToRepositoryRelativePath(It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Returns((string? path, out string relativePath) =>
            {
                relativePath = NormalizeRepositoryPath(path);
                return !string.IsNullOrWhiteSpace(relativePath);
            });
        workspacePaths.Setup(candidate => candidate.ToRepositoryRelativePath(It.IsAny<string>()))
            .Returns((string? path) => NormalizeRepositoryPath(path));
        return workspacePaths;
    }

    private static string NormalizeRepositoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalizedPath = path.Replace('\\', '/');
        if (string.Equals(normalizedPath, "/repo", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return normalizedPath.StartsWith("/repo/", StringComparison.Ordinal)
            ? normalizedPath["/repo/".Length..]
            : normalizedPath.Trim('/');
    }

    private static UpdateWorkspaceFilesCommandHandler CreateHandler(
        IEnumerable<ILanguageExtractor>? extractors = null,
        IEmbeddingGenerator? embeddingGenerator = null,
        IKnowledgeGraphRepository? repository = null,
        IIndexingWorkspacePaths? workspacePaths = null,
        IWorkspaceFileDiscoverer? workspaceFileDiscoverer = null,
        SharpSenseCliOptions? options = null)
        => new(
            extractors ?? [],
            embeddingGenerator ?? new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object,
            repository ?? new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict).Object,
            workspacePaths ?? new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict).Object,
            workspaceFileDiscoverer ?? new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
            Options.Create(options ?? new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            }));
}
