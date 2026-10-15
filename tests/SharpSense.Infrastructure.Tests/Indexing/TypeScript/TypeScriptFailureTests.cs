using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Shared.Models;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;
using static SharpSense.Infrastructure.Tests.Indexing.TypeScript.TypeScriptTestPaths;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TypeScriptFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenDiscoveryCannotReadFiles_ThenReturnsFailureWithoutRunningPasses(bool accessDenied)
    {
        var files = CreateFiles();
        Exception exception = accessDenied
            ? new UnauthorizedAccessException("Source access denied.")
            : new IOException("Source storage unavailable.");
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        var extractor = CreateExtractor(files, pass.Object, FailingDiscovery(exception));

        var result = await extractor.Extract(new(Absolute("/repo"), null), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.Should().ContainSingle().Which.Should().BeOfType<ServiceError>().Subject;
        error.ErrorCode.Should().Be(ServiceErrorCode.ThirdPartyError);
        error.Message.Should().Contain(exception.Message);
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);
    }

    [Fact]
    public async Task WhenDiscoveredFileDisappears_ThenReportsItsPathWithoutRunningPasses()
    {
        var files = CreateFiles();
        var discovery = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        discovery
            .Setup(candidate => candidate.GetAllowedFiles(
                Absolute("/repo"),
                It.IsAny<IReadOnlyList<string>>(),
                TestContext.Current.CancellationToken))
            .ReturnsAsync([new DiscoveredFile(Absolute("/repo/missing.ts"), "missing.ts")]);
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        var extractor = CreateExtractor(files, pass.Object, discovery.Object);

        var result = await extractor.Extract(new(Absolute("/repo"), null), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.Should().ContainSingle().Which.Should().BeOfType<ServiceError>().Subject;
        error.ErrorCode.Should().Be(ServiceErrorCode.ThirdPartyError);
        error.Metadata["filePath"].Should().Be("missing.ts");
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);
    }

    [Theory]
    [InlineData("{", "TypeScript extraction failed")]
    [InlineData("""{"extends":"./missing.json"}""", "not found")]
    [InlineData("""{"extends":"./tsconfig.json"}""", "circular tsconfig")]
    [InlineData("""{"extends":[42]}""", "must be strings")]
    [InlineData("""{"extends":42}""", "must be a string or an array of strings")]
    public async Task WhenConfigurationIsInvalid_ThenReturnsFailureAndRecoversAfterCorrection(
        string configuration,
        string expectedMessage)
    {
        var files = CreateFiles();
        var configPath = Absolute("/repo/tsconfig.json");
        files.AddFile(configPath, new MockFileData(configuration));
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        pass
            .Setup(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()));
        var extractor = CreateExtractor(files, pass.Object);

        var result = await extractor.Extract(new(configPath, null), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.Should().ContainSingle().Which.Should().BeOfType<ServiceError>().Subject;
        error.ErrorCode.Should().Be(ServiceErrorCode.ThirdPartyError);
        error.Message.Should().Contain(expectedMessage);
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);

        files.File.WriteAllText(configPath, "{}");

        var recovered = await extractor.Extract(new(configPath, null), TestContext.Current.CancellationToken);

        recovered.IsSuccess.Should().BeTrue();
        recovered.Value.InputPaths.Should().Contain(Absolute("/repo/app.ts"));
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Once);
    }

    [Fact]
    public async Task WhenDiscoveryHasAnUnexpectedFailure_ThenPreservesTheOriginalException()
    {
        var exception = new InvalidOperationException("Unexpected discovery failure.");
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        var extractor = CreateExtractor(CreateFiles(), pass.Object, FailingDiscovery(exception));

        var action = () => extractor.Extract(new(Absolute("/repo"), null), TestContext.Current.CancellationToken);

        var failure = await action.Should().ThrowExactlyAsync<InvalidOperationException>();
        failure.Which.Should().BeSameAs(exception);
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenExtractionPassThrows_ThenPreservesTheOriginalException(bool ioFailure)
    {
        Exception exception = ioFailure
            ? new IOException("Unexpected extraction I/O failure.")
            : new InvalidOperationException("Unexpected extraction failure.");
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        pass
            .Setup(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()))
            .Throws(exception);
        var extractor = CreateExtractor(CreateFiles(), pass.Object);

        var action = () => extractor.Extract(new(Absolute("/repo"), null), TestContext.Current.CancellationToken);

        var failure = await action.Should().ThrowAsync<Exception>();
        failure.Which.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task WhenProgressObserverThrows_ThenPreservesTheOriginalException()
    {
        var exception = new IOException("Unexpected progress observer failure.");
        var progress = new Mock<IProgress<IndexingProgress>>(MockBehavior.Strict);
        progress
            .Setup(candidate => candidate.Report(It.IsAny<IndexingProgress>()))
            .Throws(exception);
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        var extractor = CreateExtractor(CreateFiles(), pass.Object);

        var action = () => extractor.Extract(new(Absolute("/repo"), progress.Object), TestContext.Current.CancellationToken);

        var failure = await action.Should().ThrowExactlyAsync<IOException>();
        failure.Which.Should().BeSameAs(exception);
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);
    }

    [Fact]
    public async Task WhenDiscoveryCancelsWithAnotherToken_ThenPreservesCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var exception = new OperationCanceledException(cancellation.Token);
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        var extractor = CreateExtractor(CreateFiles(), pass.Object, FailingDiscovery(exception));

        var action = () => extractor.Extract(new(Absolute("/repo"), null), TestContext.Current.CancellationToken);

        var failure = await action.Should().ThrowExactlyAsync<OperationCanceledException>();
        failure.Which.Should().BeSameAs(exception);
        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);
    }

    [Fact]
    public async Task WhenAlreadyCancelledAndTargetIsMissing_ThenPreservesCancellation()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        var extractor = CreateExtractor(CreateFiles(), pass.Object);

        var action = () => extractor.Extract(new(Absolute("/repo/missing"), null), cancellation.Token);

        var failure = await action.Should().ThrowExactlyAsync<OperationCanceledException>();
        failure.Which.CancellationToken.Should().Be(cancellation.Token);
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);
    }

    private static MockFileSystem CreateFiles()
        => new(
            new Dictionary<string, MockFileData>
            {
                [Absolute("/repo/app.ts")] = new("export const value = 1;")
            },
            Absolute("/repo"));

    private static TypeScriptLanguageExtractor CreateExtractor(
        MockFileSystem files,
        ITypeScriptExtractionPass pass,
        IWorkspaceFileDiscoverer? discovery = null)
    {
        var workspace = new RepositoryWorkspace(Absolute("/repo"), Absolute("/test-storage/index.db"), files);
        var resolver = new TsConfigResolver(workspace, files);
        var discoverer = new TypeScriptSourceDiscoverer(
            workspace,
            discovery ?? new WorkspaceFileDiscoverer(workspace, files),
            files,
            resolver);

        return new TypeScriptLanguageExtractor(discoverer, files, [pass]);
    }

    private static IWorkspaceFileDiscoverer FailingDiscovery(Exception exception)
    {
        var discovery = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        discovery
            .Setup(candidate => candidate.GetAllowedFiles(
                Absolute("/repo"),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        return discovery.Object;
    }
}
