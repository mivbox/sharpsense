using AwesomeAssertions;
using Moq;
using SharpSense.Application.Refactoring;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;

namespace SharpSense.IntegrationTests;

public sealed class NodeRefactorerTests
{
    [Fact]
    public async Task WhenNodeIdIsNotPositive_ThenItReturnsFailure()
    {
        var targetLookup = new Mock<IRefactorTargetLookup>(MockBehavior.Strict);
        var workspaceRefactorer = new Mock<IWorkspaceRefactorer>(MockBehavior.Strict);
        var nodeRefactorer = new NodeRefactorer(
            targetLookup.Object,
            workspaceRefactorer.Object);

        var result = await nodeRefactorer.RefactorNode(
            0,
            "public void Updated() { }",
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Node id must be greater than zero.");
        targetLookup.VerifyNoOtherCalls();
        workspaceRefactorer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenTargetNodeDoesNotExist_ThenItReturnsFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var targetLookup = new Mock<IRefactorTargetLookup>(MockBehavior.Strict);
        var workspaceRefactorer = new Mock<IWorkspaceRefactorer>(MockBehavior.Strict);
        targetLookup.Setup(candidate => candidate.GetTarget(42, ct))
            .ReturnsAsync((RefactorTarget?)null);
        var nodeRefactorer = new NodeRefactorer(
            targetLookup.Object,
            workspaceRefactorer.Object);

        var result = await nodeRefactorer.RefactorNode(
            42,
            "public void Updated() { }",
            ct: ct);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No persisted node exists for id 42.");
        targetLookup.Verify(candidate => candidate.GetTarget(42, ct), Times.Once);
        workspaceRefactorer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenTargetNodeExists_ThenItDelegatesToWorkspaceRefactorer()
    {
        var ct = TestContext.Current.CancellationToken;
        var replacementCode = "public void Updated() { }";
        var target = new RefactorTarget(
            42,
            "src/Fixture.App/Feature.cs",
            10,
            16);
        var expectedResult = new RefactorResult(
            true,
            ["src/Fixture.App/Feature.cs"],
            string.Empty);
        var targetLookup = new Mock<IRefactorTargetLookup>(MockBehavior.Strict);
        var workspaceRefactorer = new Mock<IWorkspaceRefactorer>(MockBehavior.Strict);
        targetLookup.Setup(candidate => candidate.GetTarget(42, ct))
            .ReturnsAsync(target);
        workspaceRefactorer.Setup(candidate => candidate.RefactorNode(
                target,
                replacementCode,
                "SharpSense.sln",
                ct))
            .ReturnsAsync(expectedResult);
        var nodeRefactorer = new NodeRefactorer(
            targetLookup.Object,
            workspaceRefactorer.Object);

        var result = await nodeRefactorer.RefactorNode(
            42,
            replacementCode,
            "SharpSense.sln",
            ct);

        result.Should().Be(expectedResult);
        targetLookup.Verify(candidate => candidate.GetTarget(42, ct), Times.Once);
        workspaceRefactorer.Verify(candidate => candidate.RefactorNode(
            target,
            replacementCode,
            "SharpSense.sln",
            ct), Times.Once);
    }
}
