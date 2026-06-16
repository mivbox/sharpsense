using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Features.Indexing.IndexTarget;

public sealed class IndexTargetCommandHandlerTests
{
    [Fact]
    public async Task WhenHandleWithValidCommand_ThenPersistsNormalizedGraphWithEmbeddings()
    {
        var expectedEmbedding = new[] { 1f, 2f };
        var command = new IndexTargetCommand();
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("csharp");
        extractor.Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                CancellationToken.None))
            .ReturnsAsync(new ExtractedNodes(
                [
                    new IndexedProject("project-app", "App", "/repo/src/App/App.csproj", "hash-1")
                ],
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
                [
                    new IndexedDependency(
                        "code:project-app:App.Feature.Run()",
                        "code:project-app:App.Dependency.Execute()",
                        EdgeType.MethodCall)
                ],
                []));
        var embeddingGenerator = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddingGenerator.Setup(candidate => candidate.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { expectedSearchText })),
                null,
                CancellationToken.None))
            .ReturnsAsync([new TextEmbedding(expectedSearchText, expectedEmbedding)]);
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetPersistedCodeNodes(CancellationToken.None))
            .ReturnsAsync([]);
        repository.Setup(candidate => candidate.ReplaceTarget(
                It.Is<ExtractedNodes>(payload =>
                    payload.Projects.Single().RelativeFilePath == "src/App/App.csproj" &&
                    payload.CodeNodes.Single().RelativeFilePath == "src/App/Feature.cs" &&
                    payload.CodeNodes.Single().VectorEmbedding!.SequenceEqual(expectedEmbedding)),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths.Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths.Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/App.csproj"))
            .Returns("src/App/App.csproj");
        workspacePaths.Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/Feature.cs"))
            .Returns("src/App/Feature.cs");
        var handler = CreateHandler(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            workspacePaths.Object,
            new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            });

        await handler.Handle(command, CancellationToken.None);

        extractor.Verify(candidate => candidate.Extract(It.IsAny<ExtractionContext>(), CancellationToken.None), Times.Once);
        embeddingGenerator.Verify(candidate => candidate.GenerateBatch(It.IsAny<IEnumerable<string>>(), null, CancellationToken.None), Times.Once);
        repository.Verify(candidate => candidate.GetPersistedCodeNodes(CancellationToken.None), Times.Once);
        repository.Verify(candidate => candidate.ReplaceTarget(It.IsAny<ExtractedNodes>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WhenHandleWithMatchingPersistedFingerprint_ThenReusesStoredVector()
    {
        var expectedEmbedding = new[] { 9f, 4f };
        const string bodyHash = "hash-1";
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new IndexTargetCommand();
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
        repository.Setup(candidate => candidate.GetPersistedCodeNodes(CancellationToken.None))
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
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths.Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths.Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/Feature.cs"))
            .Returns("src/App/Feature.cs");
        var handler = CreateHandler(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            workspacePaths.Object,
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
        var expectedEmbedding = new[] { 7f, 8f };
        const string bodyHash = "hash-1";
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new IndexTargetCommand();
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
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths.Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths.Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/Feature.cs"))
            .Returns("src/App/Feature.cs");
        var handler = CreateHandler(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            workspacePaths.Object,
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

    private static IndexTargetCommandHandler CreateHandler(
        IEnumerable<ILanguageExtractor>? extractors = null,
        IEmbeddingGenerator? embeddingGenerator = null,
        IKnowledgeGraphRepository? repository = null,
        IIndexingWorkspacePaths? workspacePaths = null,
        SharpSenseCliOptions? options = null)
        => new(
            extractors ?? [],
            embeddingGenerator ?? new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object,
            repository ?? new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict).Object,
            workspacePaths ?? new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict).Object,
            Options.Create(options ?? new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            }));
}
