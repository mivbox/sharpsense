using Moq;
using SharpSense.Application.Features.DependencyGraph.Contracts;
using SharpSense.Application.Features.DependencyGraph.GetDependencyGraph;
using SharpSense.Application.Features.DependencyGraph.Infrastructure;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Features.DependencyGraph.GetDependencyGraph;

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
        var query = new GetDependencyGraphQuery();
        var expected = new GraphResult([], []);
        var repository = new Mock<IDependencyGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetGraph(CancellationToken.None))
            .ReturnsAsync(expected);
        var handler = new GetDependencyGraphQueryHandler(repository.Object);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Same(expected, result);
        repository.Verify(candidate => candidate.GetGraph(CancellationToken.None), Times.Once);
    }
}
