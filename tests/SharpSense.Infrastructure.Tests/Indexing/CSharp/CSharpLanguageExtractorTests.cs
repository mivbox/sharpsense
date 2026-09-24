using AwesomeAssertions;
using FluentResults;
using Microsoft.CodeAnalysis;
using Moq;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.CodeAnalysis;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Indexing.CSharp;

public sealed class CSharpLanguageExtractorTests
{
    [Theory]
    [InlineData("/repo/tsconfig.json")]
    [InlineData("/repo/client")]
    public async Task WhenNonCSharpTargetReceivesCSharpChange_ThenDoesNotLoadRoslyn(string target)
    {
        var loader = new Mock<IWorkspaceLoader>(MockBehavior.Strict);
        var resolver = new Mock<ICSharpWorkspaceTargetResolver>(MockBehavior.Strict);
        resolver.Setup(candidate => candidate.ResolveTargetPath(target)).Returns((string?)null);
        var extractor = new CSharpLanguageExtractor(loader.Object,
            new Mock<ITargetAnalysisEngine>(MockBehavior.Strict).Object,
            new Mock<IRepositoryWorkspace>(MockBehavior.Strict).Object, resolver.Object);

        var result = await extractor.ExtractIncremental(new IncrementalExtractionContext(target,
            [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/Backend/Feature.cs")]),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.CodeNodes);
        loader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenIncrementalExtractionStartsCold_ThenMergesLoaderDiagnostics()
    {
        var solution = new AdhocWorkspace().CurrentSolution;
        var workspaceLoader = new Mock<IWorkspaceLoader>(MockBehavior.Strict);
        var analysisEngine = new Mock<ITargetAnalysisEngine>(MockBehavior.Strict);
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        var expectedDiagnostics = new[] { "load diagnostic", "update diagnostic" };
        IReadOnlyList<WorkspaceFileChange> changedFiles =
        [
            new WorkspaceFileChange(
                WorkspaceFileChangeAction.Modified,
                NewPath: "/repo/src/Feature.cs")
        ];
        var targetResolver = new Mock<ICSharpWorkspaceTargetResolver>(MockBehavior.Strict);
        targetResolver.Setup(resolver => resolver.ResolveTargetPath("/repo/SharpSense.sln")).Returns("/repo/SharpSense.sln");
        var extractor = new CSharpLanguageExtractor(
            workspaceLoader.Object,
            analysisEngine.Object,
            repositoryWorkspace.Object,
            targetResolver.Object);

        workspaceLoader.Setup(loader => loader.Load(
                "/repo/SharpSense.sln",
                TestContext.Current.CancellationToken))
            .ReturnsAsync(Result.Ok(new WorkspaceLoadResult(solution, ["load diagnostic"])));
        workspaceLoader.Setup(loader => loader.UpdateDocuments(
                "/repo/SharpSense.sln",
                It.Is<IReadOnlyList<WorkspaceFileChange>>(files =>
                    files.Count == changedFiles.Count &&
                    files[0] == changedFiles[0]),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(Result.Ok(new WorkspaceLoadResult(solution, ["update diagnostic"])));
        analysisEngine.Setup(engine => engine.ExtractIncremental(
                "/repo/SharpSense.sln",
                solution,
                repositoryWorkspace.Object,
                It.Is<IReadOnlyList<WorkspaceFileChange>>(files =>
                    files.Count == changedFiles.Count &&
                    files[0] == changedFiles[0]),
                null,
                It.Is<IReadOnlyCollection<string>>(diagnostics =>
                    diagnostics.SequenceEqual(expectedDiagnostics)),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(new KnowledgeGraphExtractionPayload(
                "/repo/SharpSense.sln",
                [],
                [],
                [],
                expectedDiagnostics));

        var result = await extractor.ExtractIncremental(
            new IncrementalExtractionContext("/repo/SharpSense.sln", changedFiles),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Diagnostics.Should().Equal(expectedDiagnostics);
    }
}
