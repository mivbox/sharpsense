using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Application.Tests.Indexing.Support;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Indexing.UpdateWorkspaceFiles;

public sealed class UpdateWorkspaceFilesCommandHandlerTests : IDisposable
{
    private readonly IndexingHandlers _handlers = new();

    public void Dispose() => _handlers.Dispose();

    [Fact]
    public async Task WhenHandleWithValidCommand_ThenPersistsNormalizedIncrementalGraph()
    {
        var ct = TestContext.Current.CancellationToken;
        var expectedEmbedding = new[] { 1f, 2f };
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/repo/src/SharpSense.Infrastructure/Indexing/KnowledgeGraphRepository.cs")
            ]);
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
                        expectedSearchText)
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
            .Setup(candidate => candidate.GetPersistedCodeNodes(ct))
            .ReturnsAsync([]);
        repository
            .Setup(candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct))
            .Callback<ExtractedNodes, CancellationToken>((graph, _) => persistedGraph = graph)
            .Returns(Task.CompletedTask);
        var workspacePaths = CreateWorkspacePaths();
        var handler = _handlers.CreateIncremental(
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
        persistedGraph.CodeNodes.Single().RelativeFilePath.Should().Be("src/App/Feature.cs");
        persistedGraph.CodeNodes.Single().VectorEmbedding.Should().Equal(expectedEmbedding);
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
        var expectedEmbedding = new[] { 4f, 2f };
        const string bodyHash = "hash-1";
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: "/repo/src/App/Feature.cs")
            ]);
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
        var handler = _handlers.CreateIncremental(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            CreateWorkspacePaths().Object,
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
        var expectedEmbedding = new[] { 5f, 1f };
        const string bodyHash = "hash-1";
        const string expectedSearchText = "Feature.Run()\nRuns the feature.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: "/repo/src/App/Feature.cs")
            ]);
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
        var handler = _handlers.CreateIncremental(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            CreateWorkspacePaths().Object,
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

    [Fact]
    public async Task WhenHandleWithChangedSearchTextButMatchingBodyHash_ThenRegeneratesEmbedding()
    {
        var ct = TestContext.Current.CancellationToken;
        var expectedEmbedding = new[] { 6f, 3f };
        const string bodyHash = "hash-1";
        const string persistedSearchText = "Feature.Run()\nRuns the feature.";
        const string updatedSearchText = "Feature.Run()\nExplains the updated behavior.";
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: "/repo/src/App/Feature.cs")
            ]);
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
                        "Explains the updated behavior.",
                        updatedSearchText,
                        bodyHash)
                ],
                [],
                []));
        var embeddingGenerator = new Mock<IEmbeddingGenerator>(MockBehavior.Strict);
        embeddingGenerator
            .Setup(candidate => candidate.GenerateBatch(
                It.Is<IEnumerable<string>>(texts => texts.SequenceEqual(new[] { updatedSearchText })),
                null,
                ct))
            .ReturnsAsync([new TextEmbedding(updatedSearchText, expectedEmbedding)]);
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
                        persistedSearchText,
                        bodyHash,
                        [1f])
                ]);
        repository
            .Setup(candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct))
            .Callback<ExtractedNodes, CancellationToken>((graph, _) => persistedGraph = graph)
            .Returns(Task.CompletedTask);
        var handler = _handlers.CreateIncremental(
            [extractor.Object],
            embeddingGenerator.Object,
            repository.Object,
            CreateWorkspacePaths().Object,
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
            Times.Once);
        repository.Verify(
            candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct),
            Times.Once);
    }

    [Fact]
    public async Task WhenExtractorExpandsPartialSiblingsAndEmbeddingsAreSkipped_ThenRetainsSiblingVectors()
    {
        var ct = TestContext.Current.CancellationToken;
        var sibling = new IndexedCodeNode(
            "code:shared",
            "project:app",
            "Shared",
            "Shared",
            NodeType.Class,
            "src/App/PartA.cs",
            1,
            5,
            "Shared type",
            "Shared type",
            "same-body");
        var vector = new[] { 1f, 2f };
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor
            .SetupGet(candidate => candidate.SourceKind)
            .Returns(WorkspaceSourceKind.CSharp);
        extractor
            .Setup(candidate => candidate.Extract(It.IsAny<ExtractionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExtractedNodes([], [sibling], [], []));
        ExtractedNodes? persistedGraph = null;
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository
            .Setup(candidate => candidate.GetPersistedCodeNodes(ct))
            .ReturnsAsync(
                [
                    sibling with
                    {
                        VectorEmbedding = vector
                    }
                ]);
        repository
            .Setup(candidate => candidate.ReplaceWorkspace(
                It.IsAny<ExtractedNodes>(),
                ct))
            .Callback<ExtractedNodes, CancellationToken>((graph, _) => persistedGraph = graph)
            .Returns(Task.CompletedTask);
        var handler = _handlers.CreateIncremental(
            [extractor.Object],
            repository: repository.Object,
            workspacePaths: CreateWorkspacePaths().Object,
            options: new WorkspaceExecutionOptions
            {
                WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "SharpSense.sln")],
                RepositoryRoot = "/repo",
                SkipEmbeddings = true
            });

        var result = await handler.Handle(
            new UpdateWorkspaceFilesCommand(
                [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/src/App/PartB.cs")]),
            ct);

        result.IsSuccess.Should().BeTrue();
        persistedGraph.Should().NotBeNull();
        persistedGraph.CodeNodes.Single().VectorEmbedding.Should().Equal(vector);
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
        extractor
            .SetupGet(candidate => candidate.SourceKind)
            .Returns(WorkspaceSourceKind.CSharp);
        extractor
            .Setup(candidate => candidate.Extract(It.IsAny<ExtractionContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Fail<ExtractedNodes>("Cannot reload changed workspace"));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        var handler = _handlers.CreateIncremental(
            [extractor.Object],
            repository: repository.Object,
            workspacePaths: CreateWorkspacePaths().Object);

        var result = await handler.Handle(
            new UpdateWorkspaceFilesCommand(
                [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/" + changedPath)]),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().Contain(error => error.Message == "Cannot reload changed workspace");
        repository.VerifyNoOtherCalls();
    }

    private static Mock<IIndexingWorkspacePaths> CreateWorkspacePaths()
    {
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths
            .Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths
            .SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths
            .Setup(candidate => candidate.TryToRepositoryRelativePath(
                It.IsAny<string>(),
                out It.Ref<string>.IsAny))
            .Returns((string? path, out string relativePath) =>
            {
                relativePath = NormalizeRepositoryPath(path);

                return !string.IsNullOrWhiteSpace(relativePath);
            });
        workspacePaths
            .Setup(candidate => candidate.ToRepositoryRelativePath(It.IsAny<string>()))
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

}
