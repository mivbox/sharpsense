using AwesomeAssertions;
using Moq;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.GetNodeMemories;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Tests.Memory.GetNodeMemories;

public sealed class GetNodeMemoriesQueryHandlerTests
{
    [Fact]
    public async Task WhenIntentFilterIsSpecified_ThenReturnsMatchingNodeMemories()
    {
        var ct = TestContext.Current.CancellationToken;
        var expectedMemory = new MemoryNode(
            Guid.NewGuid(),
            "Fixture.App.MessageProvider.GetMessage()",
            "hash-1",
            "Security review",
            "content-hash",
            ["security"],
            MemoryIntent.Invariant,
            DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
            false);
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        memoryRepository
            .Setup(candidate => candidate.GetNodeMemories(
                It.Is<IReadOnlyCollection<int>>(nodeIds => nodeIds.Count == 1 && nodeIds.Contains(42)),
                It.Is<IReadOnlyCollection<MemoryIntent>?>(intents => intents != null && intents.SequenceEqual(new[] { MemoryIntent.Invariant })),
                ct))
            .ReturnsAsync(new Dictionary<int, MemoryNode[]>
            {
                [42] = [expectedMemory]
            });
        var handler = new GetNodeMemoriesQueryHandler(memoryRepository.Object);

        var result = await handler.Handle(new GetNodeMemoriesQuery(42, [MemoryIntent.Invariant]), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be(expectedMemory);
        memoryRepository.VerifyAll();
    }

    [Fact]
    public async Task WhenNodeDoesNotExist_ThenReturnsFailedResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        memoryRepository
            .Setup(candidate => candidate.GetNodeMemories(
                It.Is<IReadOnlyCollection<int>>(nodeIds => nodeIds.Count == 1 && nodeIds.Contains(42)),
                It.IsAny<IReadOnlyCollection<MemoryIntent>?>(),
                ct))
            .ReturnsAsync(new Dictionary<int, MemoryNode[]>());
        var handler = new GetNodeMemoriesQueryHandler(memoryRepository.Object);

        var result = await handler.Handle(new GetNodeMemoriesQuery(42), ct);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be("No persisted node exists for id 42.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task WhenNodeIdIsNotPositive_ThenReturnsFailedResult(int nodeId)
    {
        var memoryRepository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        var handler = new GetNodeMemoriesQueryHandler(memoryRepository.Object);

        var result = await handler.Handle(new GetNodeMemoriesQuery(nodeId), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should().Be("NodeId must be greater than zero.");
        memoryRepository.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task WhenExistingNodeHasNoMemories_ThenReturnsSuccessfulEmptyResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        repository
            .Setup(candidate => candidate.GetNodeMemories(
                It.Is<IReadOnlyCollection<int>>(ids => ids.SequenceEqual(new[] { 42 })),
                null,
                ct))
            .ReturnsAsync(new Dictionary<int, MemoryNode[]> { [42] = [] });
        var handler = new GetNodeMemoriesQueryHandler(repository.Object);

        var result = await handler.Handle(new GetNodeMemoriesQuery(42), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

}
