using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Features.Memory.AttachMemory;

public sealed class AttachMemoryCommandHandlerTests
{
    [Fact]
    public void WhenConstructed_ThenImplementsCommandHandlerContract()
    {
        var handler = new AttachMemoryCommandHandler(new Mock<IMemoryRepository>(MockBehavior.Strict).Object);

        handler.Should().BeAssignableTo<ICommandHandler<AttachMemoryCommand, Result>>();
    }

    [Fact]
    public async Task WhenHandleInvoked_ThenDelegatesToMemoryRepository()
    {
        var tags = new[] { "security" };
        var command = new AttachMemoryCommand(42, "Security review", tags);
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        memoryRepository.Setup(candidate => candidate.AttachMemory(
                42,
                "Security review",
                It.Is<string[]?>(candidateTags => candidateTags != null && candidateTags.SequenceEqual(tags)),
                SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent.Convention,
                CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        var handler = new AttachMemoryCommandHandler(memoryRepository.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        memoryRepository.VerifyAll();
    }
}
