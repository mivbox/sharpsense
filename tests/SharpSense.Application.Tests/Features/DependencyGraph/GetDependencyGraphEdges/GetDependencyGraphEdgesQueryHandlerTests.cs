using Moq;
using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.GetDependencyGraphEdges;
using SharpSense.Application.DependencyGraph.GetDependencyGraphEdges.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Features.DependencyGraph.GetDependencyGraphEdges;

public sealed class GetDependencyGraphEdgesQueryHandlerTests
{
    [Fact]
    public void WhenConstructingGetDependencyGraphEdgesQueryHandler_ThenImplementsQueryHandlerContract()
    {
        var handler = new GetDependencyGraphEdgesQueryHandler(new Mock<IDependencyGraphRepository>(MockBehavior.Strict).Object);

        Assert.IsAssignableFrom<IQueryHandler<GetDependencyGraphEdgesQuery, IAsyncEnumerable<GraphEdge>>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidQuery_ThenInvokesRepository()
    {
        var query = new GetDependencyGraphEdgesQuery([42]);
        var expected = StubGraphEdges();
        var repository = new Mock<IDependencyGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetGraphEdges(query.DirectoryIds, CancellationToken.None))
            .Returns(expected);
        var handler = new GetDependencyGraphEdgesQueryHandler(repository.Object);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Same(expected, result);
        repository.Verify(candidate => candidate.GetGraphEdges(query.DirectoryIds, CancellationToken.None), Times.Once);
    }

    private static async IAsyncEnumerable<GraphEdge> StubGraphEdges()
    {
        yield return new GraphEdge("edge-1", "node-1", "node-2", "methodcall", "internal");
        await Task.CompletedTask;
    }
}
