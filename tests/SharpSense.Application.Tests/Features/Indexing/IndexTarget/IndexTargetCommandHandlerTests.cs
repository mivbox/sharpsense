using Moq;
using SharpSense.Application.Features.Indexing.IndexTarget;
using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Features.Indexing.IndexTarget;

public sealed class IndexTargetCommandHandlerTests
{
    [Fact]
    public void WhenConstructingIndexTargetCommandHandler_ThenImplementsCommandHandlerContract()
    {
        var handler = new IndexTargetCommandHandler(new Mock<IKnowledgeGraphIndexing>(MockBehavior.Strict).Object);

        Assert.IsAssignableFrom<ICommandHandler<IndexTargetCommand>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidCommand_ThenInvokesFullIndexing()
    {
        var command = new IndexTargetCommand();
        var indexing = new Mock<IKnowledgeGraphIndexing>(MockBehavior.Strict);
        indexing.Setup(candidate => candidate.Index(command, CancellationToken.None))
            .Returns(Task.CompletedTask);
        var handler = new IndexTargetCommandHandler(indexing.Object);

        await handler.Handle(command, CancellationToken.None);

        indexing.Verify(candidate => candidate.Index(command, CancellationToken.None), Times.Once);
    }
}
