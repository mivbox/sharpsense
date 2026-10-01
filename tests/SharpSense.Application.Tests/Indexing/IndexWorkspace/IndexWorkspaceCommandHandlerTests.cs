using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Application.Tests.Indexing.Support;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Indexing.IndexWorkspace;

public sealed class IndexWorkspaceCommandHandlerTests : IDisposable
{
    private readonly IndexingHandlers _handlers = new();

    public void Dispose() => _handlers.Dispose();

    [Fact]
    public async Task WhenHandleWithValidCommand_ThenPersistsNormalizedGraphWithEmbeddings()
    {
        var ct = TestContext.Current.CancellationToken;
        var expectedEmbedding = new[] { 1f, 2f };
        var command = new IndexWorkspaceCommand();
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor
            .SetupGet(candidate => candidate.SourceKind)
            .Returns(WorkspaceSourceKind.CSharp);
        extractor
            .Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                It.IsAny<CancellationToken>()))
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
        embeddingGenerator
            .Setup(candidate => candidate.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { expectedSearchText })),
                null,
                ct))
            .ReturnsAsync([new TextEmbedding(expectedSearchText, expectedEmbedding)]);
        ExtractedNodes? persistedGraph = null;
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository
            .Setup(candidate => candidate.GetPersistedCodeNodes(ct))
            .ReturnsAsync([]);
        repository
            .Setup(candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct))
            .Callback<ExtractedNodes, CancellationToken>((graph, _) => persistedGraph = graph)
            .Returns(Task.CompletedTask);
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths
            .SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths
            .Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths
            .Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/App.csproj"))
            .Returns("src/App/App.csproj");
        workspacePaths
            .Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/Feature.cs"))
            .Returns("src/App/Feature.cs");
        workspacePaths
            .Setup(candidate => candidate.ToRepositoryRelativePath("/repo/SharpSense.sln"))
            .Returns("SharpSense.sln");
        var handler = _handlers.Create(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            workspacePaths.Object,
            new WorkspaceExecutionOptions
            {
                WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "SharpSense.sln")],
                RepositoryRoot = "/repo"
            });

        var result = await handler.Handle(command, ct);

        result.IsSuccess.Should().BeTrue();
        persistedGraph.Should().NotBeNull();
        persistedGraph.Projects.Single().RelativeFilePath.Should().Be("src/App/App.csproj");
        persistedGraph.CodeNodes.Single().RelativeFilePath.Should().Be("src/App/Feature.cs");
        persistedGraph.CodeNodes.Single().VectorEmbedding.Should().Equal(expectedEmbedding);
        extractor.Verify(
            candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        embeddingGenerator.Verify(
            candidate => candidate.GenerateBatch(
                It.IsAny<IEnumerable<string>>(),
                null,
                ct),
            Times.Once);
        repository.Verify(
            candidate => candidate.GetPersistedCodeNodes(ct),
            Times.Once);
        repository.Verify(
            candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct),
            Times.Once);
    }

    [Fact]
    public async Task WhenHandleWithMatchingPersistedFingerprint_ThenReusesStoredVector()
    {
        var ct = TestContext.Current.CancellationToken;
        var expectedEmbedding = new[] { 9f, 4f };
        const string bodyHash = "hash-1";
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new IndexWorkspaceCommand();
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor
            .SetupGet(candidate => candidate.SourceKind)
            .Returns(WorkspaceSourceKind.CSharp);
        extractor
            .Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                It.IsAny<CancellationToken>()))
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
        ExtractedNodes? persistedGraph = null;
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository
            .Setup(candidate => candidate.GetPersistedCodeNodes(ct))
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
        repository
            .Setup(candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct))
            .Callback<ExtractedNodes, CancellationToken>((graph, _) => persistedGraph = graph)
            .Returns(Task.CompletedTask);
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths
            .SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths
            .Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths
            .Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/Feature.cs"))
            .Returns("src/App/Feature.cs");
        workspacePaths
            .Setup(candidate => candidate.ToRepositoryRelativePath("/repo/SharpSense.sln"))
            .Returns("SharpSense.sln");
        var handler = _handlers.Create(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            workspacePaths.Object,
            new WorkspaceExecutionOptions
            {
                WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "SharpSense.sln")],
                RepositoryRoot = "/repo"
            });

        var result = await handler.Handle(command, ct);

        result.IsSuccess.Should().BeTrue();
        persistedGraph.Should().NotBeNull();
        persistedGraph.CodeNodes.Single().VectorEmbedding.Should().Equal(expectedEmbedding);
        embeddingGenerator.Verify(
            candidate => candidate.GenerateBatch(
                It.IsAny<IEnumerable<string>>(),
                null,
                ct),
            Times.Never);
        repository.Verify(
            candidate => candidate.GetPersistedCodeNodes(ct),
            Times.Once);
        repository.Verify(
            candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct),
            Times.Once);
    }

    [Fact]
    public async Task WhenHandleWithNoCacheAndMatchingPersistedFingerprint_ThenRegeneratesEmbedding()
    {
        var ct = TestContext.Current.CancellationToken;
        var expectedEmbedding = new[] { 7f, 8f };
        const string bodyHash = "hash-1";
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new IndexWorkspaceCommand();
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor
            .SetupGet(candidate => candidate.SourceKind)
            .Returns(WorkspaceSourceKind.CSharp);
        extractor
            .Setup(candidate => candidate.Extract(
                It.IsAny<ExtractionContext>(),
                It.IsAny<CancellationToken>()))
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
        embeddingGenerator
            .Setup(candidate => candidate.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { expectedSearchText })),
                null,
                ct))
            .ReturnsAsync([new TextEmbedding(expectedSearchText, expectedEmbedding)]);
        ExtractedNodes? persistedGraph = null;
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository
            .Setup(candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct))
            .Callback<ExtractedNodes, CancellationToken>((graph, _) => persistedGraph = graph)
            .Returns(Task.CompletedTask);
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths
            .SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths
            .Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths
            .Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/Feature.cs"))
            .Returns("src/App/Feature.cs");
        workspacePaths
            .Setup(candidate => candidate.ToRepositoryRelativePath("/repo/SharpSense.sln"))
            .Returns("SharpSense.sln");
        var handler = _handlers.Create(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            workspacePaths.Object,
            new WorkspaceExecutionOptions
            {
                WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "SharpSense.sln")],
                RepositoryRoot = "/repo",
                DisableEmbeddingCache = true
            });

        var result = await handler.Handle(command, ct);

        result.IsSuccess.Should().BeTrue();
        persistedGraph.Should().NotBeNull();
        persistedGraph.CodeNodes.Single().VectorEmbedding.Should().Equal(expectedEmbedding);
        embeddingGenerator.Verify(
            candidate => candidate.GenerateBatch(
                It.IsAny<IEnumerable<string>>(),
                null,
                ct),
            Times.Once);
        repository.Verify(
            candidate => candidate.GetPersistedCodeNodes(ct),
            Times.Never);
        repository.Verify(
            candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct),
            Times.Once);
    }

}
