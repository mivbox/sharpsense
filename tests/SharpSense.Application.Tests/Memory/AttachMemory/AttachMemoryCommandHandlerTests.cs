using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.AttachMemory;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Shared.Errors;

namespace SharpSense.Application.Tests.Memory.AttachMemory;

public sealed class AttachMemoryCommandHandlerTests
{
    [Fact]
    public async Task WhenTagsContainNull_ThenRejectsMemoryBeforePersistence()
    {
        var repository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        var handler = new AttachMemoryCommandHandler(repository.Object);

        var result = await handler.Handle(
            new AttachMemoryCommand(42, "Review note", [null!]),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ServiceError>()
            .Which.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenMemoryIsValid_ThenPersistsContentTagsAndIntent()
    {
        var ct = TestContext.Current.CancellationToken;
        var tags = new[] { "security" };
        var command = new AttachMemoryCommand(42, "Security review", tags, Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant);
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        memoryRepository
            .Setup(candidate => candidate.AttachMemory(
                42,
                "Security review",
                It.Is<string[]?>(candidateTags => candidateTags != null && candidateTags.SequenceEqual(tags)),
                Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant,
                ct))
            .ReturnsAsync(Result.Ok());
        var handler = new AttachMemoryCommandHandler(memoryRepository.Object);

        var result = await handler.Handle(command, ct);

        result.IsSuccess.Should().BeTrue();
        memoryRepository.VerifyAll();
    }
}
