using Moq;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Application.Features.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Features.Indexing.UpdateWorkspaceFiles;

public sealed class UpdateWorkspaceFilesCommandHandlerTests
{
    [Fact]
    public void WhenConstructingUpdateWorkspaceFilesCommandHandler_ThenImplementsCommandHandlerContract()
    {
        var handler = new UpdateWorkspaceFilesCommandHandler(new Mock<IKnowledgeGraphIndexing>(MockBehavior.Strict).Object);

        Assert.IsAssignableFrom<ICommandHandler<UpdateWorkspaceFilesCommand>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidCommand_ThenInvokesIncrementalIndexing()
    {
        var command = new UpdateWorkspaceFilesCommand(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/repo/src/SharpSense.Infrastructure/Indexing/KnowledgeGraphIndexing.cs")
            ]);
        var indexing = new Mock<IKnowledgeGraphIndexing>(MockBehavior.Strict);
        indexing.Setup(candidate => candidate.UpdateIncremental(command, CancellationToken.None))
            .Returns(Task.CompletedTask);
        var handler = new UpdateWorkspaceFilesCommandHandler(indexing.Object);

        await handler.Handle(command, CancellationToken.None);

        indexing.Verify(candidate => candidate.UpdateIncremental(command, CancellationToken.None), Times.Once);
    }
}
