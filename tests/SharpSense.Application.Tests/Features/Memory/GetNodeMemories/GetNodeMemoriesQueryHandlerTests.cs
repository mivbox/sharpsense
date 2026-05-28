using AwesomeAssertions;
using Moq;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.GetNodeMemories;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Application.Tests.Memory.GetNodeMemories;

public sealed class GetNodeMemoriesQueryHandlerTests
{
    [Fact]
    public void WhenConstructed_ThenImplementsQueryHandlerContract()
    {
        var handler = new GetNodeMemoriesQueryHandler(new Mock<IMemoryReader>(MockBehavior.Strict).Object);

        handler.Should().BeAssignableTo<IQueryHandler<GetNodeMemoriesQuery, MemoryNode[]>>();
    }

    [Fact]
    public async Task WhenNodeExists_ThenReturnsReaderResults()
    {
        var expectedMemory = new MemoryNode(
            Guid.NewGuid(),
            "Fixture.App.MessageProvider.GetMessage()",
            "hash-1",
            "Security review",
            "content-hash",
            ["security"],
            DateTimeOffset.Parse("2026-05-14T00:00:00+00:00"),
            false);
        var memoryReader = new Mock<IMemoryReader>(MockBehavior.Strict);
        memoryReader.Setup(candidate => candidate.GetNodeMemories(
                It.Is<IReadOnlyCollection<int>>(nodeIds => nodeIds.Count == 1 && nodeIds.Contains(42)),
                CancellationToken.None))
            .ReturnsAsync(new Dictionary<int, MemoryNode[]>
            {
                [42] = [expectedMemory]
            });
        var handler = new GetNodeMemoriesQueryHandler(memoryReader.Object);

        var result = await handler.Handle(new GetNodeMemoriesQuery(42), CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Be(expectedMemory);
        memoryReader.VerifyAll();
    }

    [Fact]
    public async Task WhenNodeDoesNotExist_ThenThrowsInvalidOperationException()
    {
        var memoryReader = new Mock<IMemoryReader>(MockBehavior.Strict);
        memoryReader.Setup(candidate => candidate.GetNodeMemories(
                It.Is<IReadOnlyCollection<int>>(nodeIds => nodeIds.Count == 1 && nodeIds.Contains(42)),
                CancellationToken.None))
            .ReturnsAsync(new Dictionary<int, MemoryNode[]>());
        var handler = new GetNodeMemoriesQueryHandler(memoryReader.Object);

        var act = async () => await handler.Handle(new GetNodeMemoriesQuery(42), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No persisted node exists for id 42.");
    }
}
