using Moq;
using SharpSense.Application.DependencyGraph.Abstractions;
using SharpSense.Application.DependencyGraph.GetDependencyGraphNodes;
using SharpSense.Application.DependencyGraph.GetDependencyGraphNodes.Models;
using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Features.DependencyGraph.GetDependencyGraphNodes;

public sealed class GetDependencyGraphNodesQueryHandlerTests
{
    [Fact]
    public void WhenConstructingGetDependencyGraphNodesQueryHandler_ThenImplementsQueryHandlerContract()
    {
        var handler = new GetDependencyGraphNodesQueryHandler(new Mock<IDependencyGraphRepository>(MockBehavior.Strict).Object);

        Assert.IsAssignableFrom<IQueryHandler<GetDependencyGraphNodesQuery, IAsyncEnumerable<GraphNode>>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidQuery_ThenInvokesRepository()
    {
        var query = new GetDependencyGraphNodesQuery([42]);
        var expected = StubGraphNodes();
        var repository = new Mock<IDependencyGraphRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetGraphNodes(query.DirectoryIds, CancellationToken.None))
            .Returns(expected);
        var handler = new GetDependencyGraphNodesQueryHandler(repository.Object);

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Same(expected, result);
        repository.Verify(candidate => candidate.GetGraphNodes(query.DirectoryIds, CancellationToken.None), Times.Once);
    }

    private static async IAsyncEnumerable<GraphNode> StubGraphNodes()
    {
        yield return new GraphNode("node-1", "Node 1", "class", "src/Node1.cs", null, "selected", true);
        await Task.CompletedTask;
    }
}
