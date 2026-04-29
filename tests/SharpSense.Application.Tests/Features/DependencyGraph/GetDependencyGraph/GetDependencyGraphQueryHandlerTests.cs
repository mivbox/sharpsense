using Moq;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.DependencyGraph.GetDependencyGraph;
using SharpSense.Application.DependencyGraph.GetDependencyGraph.Models;
using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.DependencyGraph.GetDependencyGraph;

public sealed class GetDependencyGraphQueryHandlerTests
{
    [Fact]
    public void WhenConstructingGetDependencyGraphQueryHandler_ThenImplementsQueryHandlerContract()
    {
        var handler = new GetDependencyGraphQueryHandler(new Mock<IDependencyGraphRepository>(MockBehavior.Strict).Object);

        Assert.IsAssignableFrom<IQueryHandler<GetDependencyGraphQuery, GraphResult>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidQuery_ThenInvokesRepository()
    {
        var query = new GetDependencyGraphQuery([42]);
        var expected = new GraphResult([], []);
        var repository = new Mock<IDependencyGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetGraph(query.DirectoryIds, query.IncludeBoundaryNodes, CancellationToken.None))
            .ReturnsAsync(expected);
        var handler = new GetDependencyGraphQueryHandler(repository.Object);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Same(expected, result);
        repository.Verify(candidate => candidate.GetGraph(query.DirectoryIds, query.IncludeBoundaryNodes, CancellationToken.None), Times.Once);
    }
}
