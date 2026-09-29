using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory;
using SharpSense.Application.Memory.AttachMemory.Models;

namespace SharpSense.Application.Tests.Memory.AttachMemory;

public sealed class AttachMemoryCommandHandlerTests
{
    [Fact]
    public async Task WhenHandleInvoked_ThenDelegatesToMemoryRepository()
    {
        var tags = new[]
        {
            "security"
        };
        var command = new AttachMemoryCommand(42, "Security review", tags);
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        memoryRepository.Setup(candidate => candidate.AttachMemory(
            42,
            "Security review",
            It.Is<string[]?>(candidateTags => candidateTags != null && candidateTags.SequenceEqual(tags)),
            Domain.KnowledgeGraph.Enums.MemoryIntent.Convention,
            CancellationToken.None))
            .ReturnsAsync(Result.Ok());
        var handler = new AttachMemoryCommandHandler(memoryRepository.Object);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        memoryRepository.VerifyAll();
    }
}
