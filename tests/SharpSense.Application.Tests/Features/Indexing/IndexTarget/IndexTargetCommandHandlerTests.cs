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

namespace SharpSense.Application.Tests.Indexing.IndexTarget;

public sealed class IndexTargetCommandHandlerTests
{
    [Fact]
    public void WhenConstructingIndexTargetCommandHandler_ThenImplementsCommandHandlerContract()
    {
        var handler = CreateHandler();

        Assert.IsAssignableFrom<ICommandHandler<IndexTargetCommand>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidCommand_ThenPersistsNormalizedGraphWithEmbeddings()
    {
        var expectedEmbedding = new[] { 1f, 2f };
        var command = new IndexTargetCommand();
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
                        "Runs the feature.")
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
                It.IsAny<IEnumerable<string>>(),
                null,
                CancellationToken.None))
            .ReturnsAsync([new TextEmbedding("App.Feature.Run()", expectedEmbedding)]);
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
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
