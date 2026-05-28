using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Memory.AttachMemory;

public sealed class AttachMemoryCommandHandlerTests
{
    [Fact]
    public void WhenConstructed_ThenImplementsCommandHandlerContract()
    {
        var handler = new AttachMemoryCommandHandler(new Mock<IMemoryWriter>(MockBehavior.Strict).Object);

        handler.Should().BeAssignableTo<ICommandHandler<AttachMemoryCommand, Result>>();
    }

    [Fact]
    public async Task WhenHandleInvoked_ThenDelegatesToMemoryWriter()
    {
        var tags = new[] { "security" };
        var command = new AttachMemoryCommand(42, "Security review", tags);
        var memoryWriter = new Mock<IMemoryWriter>(MockBehavior.Strict);
        memoryWriter.Setup(candidate => candidate.AttachMemory(
                42,
                "Security review",
                It.Is<string[]?>(candidateTags => candidateTags != null && candidateTags.SequenceEqual(tags)),
                CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        var handler = new AttachMemoryCommandHandler(memoryWriter.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        memoryWriter.VerifyAll();
    }
}
