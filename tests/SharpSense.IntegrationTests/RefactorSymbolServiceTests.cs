using AwesomeAssertions;
using Moq;
using SharpSense.Application.Refactoring;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;

namespace SharpSense.IntegrationTests;

public sealed class RefactorSymbolServiceTests
{
    [Fact]
    public async Task WhenNodeIdIsNotPositive_ThenItReturnsFailure()
    {
        var targetLookup = new Mock<IRefactorTargetLookup>(MockBehavior.Strict);
        var workspaceRenamer = new Mock<IWorkspaceRenamer>(MockBehavior.Strict);
        var service = new RefactorSymbolService(
            targetLookup.Object,
            workspaceRenamer.Object);

        var result = await service.RenameSymbol(
            0,
            "Updated",
            ct: TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Node id must be greater than zero.");
        targetLookup.VerifyNoOtherCalls();
        workspaceRenamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenTargetNodeDoesNotExist_ThenItReturnsFailure()
    {
        var ct = TestContext.Current.CancellationToken;
        var targetLookup = new Mock<IRefactorTargetLookup>(MockBehavior.Strict);
        var workspaceRenamer = new Mock<IWorkspaceRenamer>(MockBehavior.Strict);
        targetLookup.Setup(candidate => candidate.GetTarget(42, ct))
            .ReturnsAsync((NodeRefactorTarget?)null);
        var service = new RefactorSymbolService(
            targetLookup.Object,
            workspaceRenamer.Object);

        var result = await service.RenameSymbol(
            42,
            "Updated",
            ct: ct);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No persisted node exists for id 42.");
        targetLookup.Verify(candidate => candidate.GetTarget(42, ct), Times.Once);
        workspaceRenamer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenTargetNodeExists_ThenItDelegatesToWorkspaceRefactorer()
    {
        var ct = TestContext.Current.CancellationToken;
        const string newName = "Updated";
        var target = new NodeRefactorTarget(
            42,
            "src/Fixture.App/Feature.cs",
            10,
            16);
        var expectedResult = new RefactorResult(
            true,
            ["src/Fixture.App/Feature.cs"],
            string.Empty);
        var targetLookup = new Mock<IRefactorTargetLookup>(MockBehavior.Strict);
        var workspaceRenamer = new Mock<IWorkspaceRenamer>(MockBehavior.Strict);
        targetLookup.Setup(candidate => candidate.GetTarget(42, ct))
            .ReturnsAsync(target);
        workspaceRenamer.Setup(candidate => candidate.RenameSymbol(
                target,
                newName,
                "SharpSense.sln",
                ct))
            .ReturnsAsync(expectedResult);
        var service = new RefactorSymbolService(
            targetLookup.Object,
            workspaceRenamer.Object);

        var result = await service.RenameSymbol(
            42,
            newName,
            "SharpSense.sln",
            ct);

        result.Should().Be(expectedResult);
        targetLookup.Verify(candidate => candidate.GetTarget(42, ct), Times.Once);
        workspaceRenamer.Verify(candidate => candidate.RenameSymbol(
            target,
            newName,
            "SharpSense.sln",
            ct), Times.Once);
    }
}
