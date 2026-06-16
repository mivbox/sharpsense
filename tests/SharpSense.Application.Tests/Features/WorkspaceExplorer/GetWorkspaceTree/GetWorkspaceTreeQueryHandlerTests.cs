using Moq;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.WorkspaceExplorer.Abstractions;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree.Models;
using SharpSense.Application.WorkspaceExplorer.Models;

namespace SharpSense.Application.Tests.Features.WorkspaceExplorer.GetWorkspaceTree;

public sealed class GetWorkspaceTreeQueryHandlerTests
{
    [Fact]
    public void WhenConstructingHandler_ThenImplementsQueryHandlerContract()
    {
        var handler = new GetWorkspaceTreeQueryHandler(new Mock<IWorkspaceTreeRepository>(MockBehavior.Strict).Object);

        Assert.IsAssignableFrom<IQueryHandler<GetWorkspaceTreeQuery, WorkspaceTreeResult>>(handler);
    }

    [Fact]
    public async Task WhenHandlingTreePath_ThenInvokesRepository()
    {
        var query = new GetWorkspaceTreeQuery("/");
        var expected = new WorkspaceTreeResult("/", []);
        var repository = new Mock<IWorkspaceTreeRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetTree(query.Path, CancellationToken.None))
            .ReturnsAsync(expected);
        var handler = new GetWorkspaceTreeQueryHandler(repository.Object);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Same(expected, result);
        repository.Verify(candidate => candidate.GetTree(query.Path, CancellationToken.None), Times.Once);
    }
}
