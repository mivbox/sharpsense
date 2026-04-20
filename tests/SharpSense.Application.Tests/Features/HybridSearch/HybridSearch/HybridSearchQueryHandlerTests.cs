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
        var handler = new HybridSearchQueryHandler(new FakeHybridSearchService());

        Assert.IsAssignableFrom<IQueryHandler<HybridSearchQuery, HybridSearchResult>>(handler);
    }

    [Fact]
    public async Task WhenHandleWithValidQuery_ThenInvokesSearchService()
    {
        var service = new FakeHybridSearchService();
        var handler = new HybridSearchQueryHandler(service);
        var query = new HybridSearchQuery("ProjectNode");

        var result = await handler.Handle(query, CancellationToken.None);

        Assert.Same(query, service.LastQuery);
        Assert.Equal("ProjectNode", result.SearchText);
    }

    private sealed class FakeHybridSearchService : IHybridSearchService
    {
        public HybridSearchQuery? LastQuery { get; private set; }

        public Task<HybridSearchResult> Search(HybridSearchQuery query, CancellationToken ct)
        {
            LastQuery = query;
            return Task.FromResult(new HybridSearchResult(query.SearchText, []));
        }
    }
}
