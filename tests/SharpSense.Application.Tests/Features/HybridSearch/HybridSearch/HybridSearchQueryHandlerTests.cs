using Moq;
using SharpSense.Application.Features.HybridSearch.Contracts;
using SharpSense.Application.Features.HybridSearch.HybridSearch;
using SharpSense.Application.Features.HybridSearch.Infrastructure;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Tests.Features.HybridSearch.HybridSearch;

public sealed class HybridSearchQueryHandlerTests
{
    [Fact]
    public void WhenConstructingHybridSearchQueryHandler_ThenImplementsQueryHandlerContract()
    {
        var handler = new HybridSearchQueryHandler(new Mock<IHybridSearcher>(MockBehavior.Strict).Object);

        Assert.IsAssignableFrom<IQueryHandler<HybridSearchQuery, HybridSearchResult>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidQuery_ThenInvokesSearchService()
    {
        var query = new HybridSearchQuery("ProjectNode");
        var service = new Mock<IHybridSearcher>(MockBehavior.Strict);
        service.Setup(searcher => searcher.Search(query, CancellationToken.None))
            .ReturnsAsync(new HybridSearchResult(query.SearchText, []));
        var handler = new HybridSearchQueryHandler(service.Object);

        var result = await handler.Handle(query, CancellationToken.None);

        service.Verify(searcher => searcher.Search(query, CancellationToken.None), Times.Once);
        Assert.Equal("ProjectNode", result.SearchText);
    }
}
