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
        var extractor = new CSharpLanguageExtractor(
            workspaceLoader.Object,
            analysisEngine.Object,
            repositoryWorkspace.Object);

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
