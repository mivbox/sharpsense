using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TypeScriptLanguageExtractorTests
{
    [Fact]
    public async Task WhenExtractingTarget_ThenExecutesRegisteredPassesWithDiscoveredFiles()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/SharpSense.sln"] = new(""),
            ["/repo/src/App.tsx"] = new("export const App = () => <div />;")
        }, "/repo");
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace.Setup(candidate => candidate.GetRequiredTargetDirectoryPath("/repo/SharpSense.sln"))
            .Returns("/repo");
        repositoryWorkspace.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        fileDiscoverer.Setup(candidate => candidate.GetAllowedFiles(
                "/repo",
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(
            [
                new DiscoveredFile("/repo/src/App.tsx", "src/App.tsx")
            ]);
        var extractor = new TypeScriptLanguageExtractor(
            new TypeScriptSourceDiscoverer(
                repositoryWorkspace.Object,
                fileDiscoverer.Object,
                fileSystem,
                new TsConfigResolver(repositoryWorkspace.Object, fileSystem)),
            fileSystem,
            [new RecordingPass()]);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.Diagnostics.Should().Equal("src/App.tsx");
    }

    [Fact]
    public async Task WhenExtractingTarget_ThenReportsTypeScriptProgressIncrementally()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/SharpSense.sln"] = new(""),
            ["/repo/src/App.tsx"] = new("export const App = () => <div />;")
        }, "/repo");
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace.Setup(candidate => candidate.GetRequiredTargetDirectoryPath("/repo/SharpSense.sln"))
            .Returns("/repo");
        repositoryWorkspace.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        fileDiscoverer.Setup(candidate => candidate.GetAllowedFiles(
                "/repo",
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(
            [
                new DiscoveredFile("/repo/src/App.tsx", "src/App.tsx")
            ]);
        var progressUpdates = new List<IndexingProgress>();
        var extractor = new TypeScriptLanguageExtractor(
            new TypeScriptSourceDiscoverer(
                repositoryWorkspace.Object,
                fileDiscoverer.Object,
                fileSystem,
                new TsConfigResolver(repositoryWorkspace.Object, fileSystem)),
            fileSystem,
            [new RecordingPass()]);

        await extractor.Extract(
            new ExtractionContext(
                "/repo/SharpSense.sln",
                new CollectingProgress(progressUpdates)),
            TestContext.Current.CancellationToken);

        progressUpdates.Select(static update => (update.CurrentTask, update.CompletedItems, update.TotalItems))
            .Should()
            .Equal(
                ("Discovering TypeScript files...", 0, 1),
                ("Parsing src/App.tsx...", 1, 2),
                ("Running RecordingPass...", 2, 2));
    }

    [Fact]
    public async Task WhenIncrementalChangesContainNoTypeScriptFiles_ThenReturnsEmptyWithoutDiscovery()
    {
        var fileSystem = new MockFileSystem();
        var extractor = new TypeScriptLanguageExtractor(
            new TypeScriptSourceDiscoverer(
                new Mock<IRepositoryWorkspace>(MockBehavior.Strict).Object,
                new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
                fileSystem,
                new TsConfigResolver(new Mock<IRepositoryWorkspace>(MockBehavior.Strict).Object, fileSystem)),
            fileSystem,
            []);

        var result = await extractor.ExtractIncremental(
            new IncrementalExtractionContext(
                "/repo/SharpSense.sln",
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: "/repo/src/Feature.cs"),
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: "/repo/docs/Guide.md")
                ]),
            TestContext.Current.CancellationToken);

        result.Value.Projects.Should().BeEmpty();
        result.Value.CodeNodes.Should().BeEmpty();
        result.Value.Edges.Should().BeEmpty();
        result.Value.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenIncrementalTypeScriptPathLivesUnderBuildNamedRepositoryRoot_ThenItStillRunsDiscovery()
    {
        const string repositoryRoot = "/repo/build/worktree";
        const string targetPath = "/repo/build/worktree/SharpSense.sln";
        const string changedFilePath = "/repo/build/worktree/src/App.tsx";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [targetPath] = new(""),
            [changedFilePath] = new("export const App = () => <div />;")
        }, repositoryRoot);
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace.Setup(candidate => candidate.GetRequiredTargetDirectoryPath(targetPath))
            .Returns(repositoryRoot);
        repositoryWorkspace.SetupGet(candidate => candidate.RootPath)
            .Returns(repositoryRoot);
        repositoryWorkspace.Setup(candidate => candidate.IsSameOrSubPath(changedFilePath)).Returns(true);
        repositoryWorkspace.Setup(candidate => candidate.ToRepositoryRelativePath(changedFilePath)).Returns("src/App.tsx");
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        fileDiscoverer.Setup(candidate => candidate.GetAllowedFiles(
                repositoryRoot,
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(
            [
                new DiscoveredFile(changedFilePath, "src/App.tsx")
            ]);
        var extractor = new TypeScriptLanguageExtractor(
            new TypeScriptSourceDiscoverer(
                repositoryWorkspace.Object,
                fileDiscoverer.Object,
                fileSystem,
                new TsConfigResolver(repositoryWorkspace.Object, fileSystem)),
            fileSystem,
            [new RecordingPass()]);

        var result = await extractor.ExtractIncremental(
            new IncrementalExtractionContext(
                targetPath,
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: changedFilePath)
                ]),
            TestContext.Current.CancellationToken);

        result.Value.Diagnostics.Should().Equal("src/App.tsx");
    }

    [Fact]
    public async Task MissingSelectedDirectoryFailsInsteadOfReturningEmptyGraph()
    {
        var fileSystem = new MockFileSystem();
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        var extractor = new TypeScriptLanguageExtractor(
            new TypeScriptSourceDiscoverer(
                repositoryWorkspace.Object,
                new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object,
                fileSystem,
                new TsConfigResolver(repositoryWorkspace.Object, fileSystem)),
            fileSystem,
            []);

        var result = await extractor.Extract(new ExtractionContext("/repo/missing-frontend", null), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("does not exist");
    }

    private sealed class RecordingPass : ITypeScriptExtractionPass
    {
        public void Execute(TypeScriptPassContext context)
        {
            context.Diagnostics.AddRange(context.DiscoveredFiles.Select(static file => file.RelativeFilePath));
        }
    }

    private sealed class CollectingProgress(ICollection<IndexingProgress> updates) : IProgress<IndexingProgress>
    {
        public void Report(IndexingProgress value)
            => updates.Add(value);
    }
}
