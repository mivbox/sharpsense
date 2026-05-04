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
        var workspacePaths = CreateWorkspacePaths();
        var handler = CreateHandler(
            [extractor.Object],
            repository.Object,
            workspacePaths.Object,
            new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
            new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            });

        await handler.Handle(command, CancellationToken.None);

        repository.Verify(candidate => candidate.ReplaceWorkspaceFiles(It.IsAny<IReadOnlyList<string>>(), It.IsAny<ExtractedNodes>(), CancellationToken.None), Times.Once);
    }

    [Fact]
    public async Task WhenHandleWithRenamedDirectory_ThenExpandsDirectoryChangeBeforePersisting()
    {
        var expectedDiscoveryGlobs = new[] { "**/*.cs", "**/*.md", "**/*.markdown", "**/*.mdown", "**/*.mkd" };
        var expectedChangedPaths = new[] { "docs/New/Guide.md", "docs/Old/Guide.md" };
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
        extractor.Setup(candidate => candidate.ExtractIncremental(
                It.Is<IncrementalExtractionContext>(context => context.ChangedFiles.Single().ActionType == WorkspaceFileChangeAction.Renamed),
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
                        "Guide content.")
                ],
                [],
                []));
        var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetPersistedDocumentPathsUnderDirectory("docs/Old", CancellationToken.None))
            .ReturnsAsync(["docs/Old/Guide.md"]);
        repository.Setup(candidate => candidate.ReplaceWorkspaceFiles(
                It.Is<IReadOnlyList<string>>(paths => paths.SequenceEqual(expectedChangedPaths)),
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
        IKnowledgeGraphRepository? repository = null,
        IIndexingWorkspacePaths? workspacePaths = null,
        IWorkspaceFileDiscoverer? workspaceFileDiscoverer = null,
        SharpSenseCliOptions? options = null)
        => new(
            extractors ?? [],
            repository ?? new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict).Object,
            workspacePaths ?? new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict).Object,
            workspaceFileDiscoverer ?? new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
            Options.Create(options ?? new SharpSenseCliOptions
            {
                TargetPath = "SharpSense.sln",
                RepositoryRoot = "/repo"
            }));
}
