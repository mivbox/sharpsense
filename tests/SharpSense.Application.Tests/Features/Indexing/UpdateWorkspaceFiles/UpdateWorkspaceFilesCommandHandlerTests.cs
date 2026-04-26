using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Indexing.UpdateWorkspaceFiles;

public sealed class UpdateWorkspaceFilesCommandHandlerTests
{
    [Fact]
    public void WhenConstructingUpdateWorkspaceFilesCommandHandler_ThenImplementsCommandHandlerContract()
    {
        var handler = CreateHandler();

        Assert.IsAssignableFrom<ICommandHandler<UpdateWorkspaceFilesCommand>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidCommand_ThenPersistsNormalizedIncrementalGraph()
    {
        var expectedChangedPaths = new[] { "src/SharpSense.Infrastructure/Indexing/KnowledgeGraphRepository.cs" };
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/repo/src/SharpSense.Infrastructure/Indexing/KnowledgeGraphRepository.cs")
            ]);
        var extractor = new Mock<ILanguageExtractor>(MockBehavior.Strict);
        extractor.SetupGet(candidate => candidate.ExtractorName)
            .Returns("csharp");
        extractor.Setup(candidate => candidate.ExtractIncremental(
                It.IsAny<IncrementalExtractionContext>(),
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
                        "Runs the feature.")
                ],
                [],
                []));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.ReplaceWorkspaceFiles(
                It.Is<IReadOnlyList<string>>(paths => paths.SequenceEqual(expectedChangedPaths)),
                It.Is<ExtractedNodes>(payload => payload.CodeNodes.Single().RelativeFilePath == "src/App/Feature.cs"),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var workspacePaths = new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict);
        workspacePaths.Setup(candidate => candidate.GetRequiredTargetPath("SharpSense.sln"))
            .Returns("/repo/SharpSense.sln");
        workspacePaths.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        workspacePaths.Setup(candidate => candidate.TryToRepositoryRelativePath("/repo/src/SharpSense.Infrastructure/Indexing/KnowledgeGraphRepository.cs", out It.Ref<string>.IsAny))
            .Returns((string? _, out string relativePath) =>
            {
                relativePath = "src/SharpSense.Infrastructure/Indexing/KnowledgeGraphRepository.cs";
                return true;
            });
        workspacePaths.Setup(candidate => candidate.ToRepositoryRelativePath("/repo/src/App/Feature.cs"))
            .Returns("src/App/Feature.cs");
        var handler = CreateHandler(
            [extractor.Object],
            repository.Object,
            workspacePaths.Object,
            new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            });

        await handler.Handle(command, CancellationToken.None);

        repository.Verify(candidate => candidate.ReplaceWorkspaceFiles(It.IsAny<IReadOnlyList<string>>(), It.IsAny<ExtractedNodes>(), CancellationToken.None), Times.Once);
    }

    private static UpdateWorkspaceFilesCommandHandler CreateHandler(
        IEnumerable<ILanguageExtractor>? extractors = null,
        IKnowledgeGraphRepository? repository = null,
        IIndexingWorkspacePaths? workspacePaths = null,
        SharpSenseCliOptions? options = null)
        => new(
            extractors ?? [],
            repository ?? new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict).Object,
            workspacePaths ?? new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict).Object,
            Options.Create(options ?? new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            }));
}
