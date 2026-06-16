using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.GetNodeMemories;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Tests.Features.Memory.GetNodeMemories;

public sealed class GetNodeMemoriesQueryHandlerTests
{
    [Fact]
    public void WhenConstructed_ThenImplementsQueryHandlerContract()
    {
        var handler = new GetNodeMemoriesQueryHandler(new Mock<IMemoryRepository>(MockBehavior.Strict).Object);

        handler.Should().BeAssignableTo<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>();
    }

    [Fact]
    public async Task WhenNodeExists_ThenReturnsRepositoryResults()
    {
        var expectedMemory = new MemoryNode(
            Guid.NewGuid(),
            "Fixture.App.MessageProvider.GetMessage()",
            "hash-1",
            "Security review",
            "content-hash",
            ["security"],
            SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent.Invariant,
            DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
            false);
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        memoryRepository.Setup(candidate => candidate.GetNodeMemories(
                It.Is<IReadOnlyCollection<int>>(nodeIds => nodeIds.Count == 1 && nodeIds.Contains(42)),
                It.IsAny<IReadOnlyCollection<SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent>?>(),
                CancellationToken.None))
            .ReturnsAsync(new Dictionary<int, MemoryNode[]>
            {
                [42] = [expectedMemory]
            });
        var handler = new GetNodeMemoriesQueryHandler(memoryRepository.Object);

        var result = await handler.Handle(new GetNodeMemoriesQuery(42), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(expectedMemory);
        memoryRepository.VerifyAll();
    }

    [Fact]
    public async Task WhenNodeDoesNotExist_ThenReturnsFailedResult()
    {
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        memoryRepository.Setup(candidate => candidate.GetNodeMemories(
                It.Is<IReadOnlyCollection<int>>(nodeIds => nodeIds.Count == 1 && nodeIds.Contains(42)),
                It.IsAny<IReadOnlyCollection<SharpSense.Domain.KnowledgeGraph.Enums.MemoryIntent>?>(),
                CancellationToken.None))
            .ReturnsAsync(new Dictionary<int, MemoryNode[]>());
        var handler = new GetNodeMemoriesQueryHandler(memoryRepository.Object);

        var result = await handler.Handle(new GetNodeMemoriesQuery(42), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be("No persisted node exists for id 42.");
    }

    [Fact]
    public async Task WhenNodeIdIsNotPositive_ThenReturnsFailedResult()
    {
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        var handler = new GetNodeMemoriesQueryHandler(memoryRepository.Object);

        var result = await handler.Handle(new GetNodeMemoriesQuery(0), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be("NodeId must be greater than zero.");
        memoryRepository.VerifyNoOtherCalls();
    }
}
