using AwesomeAssertions;
using Moq;
using SharpSense.Application.HybridSearch.Abstractions;
using SharpSense.Application.HybridSearch.HybridSearch;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;

namespace SharpSense.Application.Tests.HybridSearch.HybridSearch;

public sealed class HybridSearchQueryHandlerTests
{
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
        result.SearchText.Should().Be("ProjectNode");
    }
}
