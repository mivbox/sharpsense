using AwesomeAssertions;
using FluentResults;
using Moq;
using SharpSense.Application.HybridSearch.Abstractions;
using SharpSense.Application.HybridSearch.HybridSearch;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Errors;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.HybridSearch.HybridSearch;

public sealed class HybridSearchQueryHandlerTests
{
    [Theory]
    [InlineData(NodeType.Memory)]
    [InlineData((NodeType)999)]
    public async Task WhenNodeTypesContainsUnsupportedValue_ThenFailsWithInvalidArgumentWithoutCallingSearcher(
        NodeType unsupportedType)
    {
        var searcher = new Mock<IHybridSearcher>(MockBehavior.Strict);
        var handler = new HybridSearchQueryHandler(searcher.Object);

        var result = await handler.Handle(
            new HybridSearchQuery("search", IncludedNodeTypes: [unsupportedType]),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ServiceError>()
            .Which.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        searcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenTagFiltersContainNull_ThenFailsWithInvalidArgumentWithoutCallingSearcher()
    {
        var searcher = new Mock<IHybridSearcher>(MockBehavior.Strict);
        var handler = new HybridSearchQueryHandler(searcher.Object);

        var result = await handler.Handle(
            new HybridSearchQuery("search", IncludeMemories: true, TagFilters: [null!]),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Should().BeOfType<ServiceError>()
            .Which.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        searcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenQueryIsValid_ThenDelegatesToSearcher()
    {
        var ct = TestContext.Current.CancellationToken;
        var query = new HybridSearchQuery(
            "search",
            Limit: 5,
            ProjectId: "SharpSense.Application",
            IncludedNodeTypes: [NodeType.Class, NodeType.Method],
            IncludeMemories: true,
            TagFilters: ["graph"]);
        var expected = new HybridSearchResult("search", []);
        var searcher = new Mock<IHybridSearcher>(MockBehavior.Strict);
        searcher
            .Setup(candidate => candidate.Search(query, ct))
            .ReturnsAsync(Result.Ok(expected));
        var handler = new HybridSearchQueryHandler(searcher.Object);

        var result = await handler.Handle(query, ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected);
        searcher.Verify(candidate => candidate.Search(query, ct), Times.Once);
    }
}
