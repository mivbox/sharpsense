using AwesomeAssertions;
using Moq;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.ListMemories;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Errors;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Memory.ListMemories;

public sealed class ListMemoriesQueryHandlerTests
{
    [Fact]
    public async Task WhenQueryHasUnnormalizedFiltersAndOversizedLimit_ThenItNormalizesBeforeQueryingRepository()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new ListMemoriesResult([], 0, 0, false);
        ListMemoriesQuery? captured = null;
        var repository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        repository
            .Setup(candidate => candidate.ListMemories(It.IsAny<ListMemoriesQuery>(), ct))
            .Callback<ListMemoriesQuery, CancellationToken>((query, _) => captured = query)
            .ReturnsAsync(expected);
        var handler = new ListMemoriesQueryHandler(repository.Object);

        var result = await handler.Handle(
            new ListMemoriesQuery(
                Intents: [MemoryIntent.Warning, MemoryIntent.Warning],
                Tags: [" SQLite ", " ", "sqlite", "Perf"],
                StaleOnly: true,
                SearchText: "  transaction  ",
                Limit: 500,
                Offset: 10),
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected);
        captured.Should().NotBeNull();
        captured!.Intents.Should().Equal(MemoryIntent.Warning);
        captured.Tags.Should().Equal("sqlite", "perf");
        captured.StaleOnly.Should().BeTrue();
        captured.SearchText.Should().Be("transaction");
        captured.Limit.Should().Be(200);
        captured.Offset.Should().Be(10);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -1)]
    public async Task WhenPagingArgumentsAreInvalid_ThenItReturnsInvalidArgumentWithoutCallingRepository(
        int limit,
        int offset)
    {
        var repository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        var handler = new ListMemoriesQueryHandler(repository.Object);

        var result = await handler.Handle(
            new ListMemoriesQuery(Limit: limit, Offset: offset),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.OfType<ServiceError>().Should().ContainSingle()
            .Which.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenIntentIsUndefinedOrTagIsNull_ThenItReturnsInvalidArgument()
    {
        var repository = new Mock<IMemoryRepository>(MockBehavior.Strict);
        var handler = new ListMemoriesQueryHandler(repository.Object);

        var invalidIntent = await handler.Handle(
            new ListMemoriesQuery(Intents: [(MemoryIntent)99]),
            TestContext.Current.CancellationToken);
        var nullTag = await handler.Handle(
            new ListMemoriesQuery(Tags: ["ok", null!]),
            TestContext.Current.CancellationToken);

        invalidIntent.IsFailed.Should().BeTrue();
        invalidIntent.Errors.OfType<ServiceError>().Should().ContainSingle()
            .Which.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        nullTag.IsFailed.Should().BeTrue();
        nullTag.Errors.OfType<ServiceError>().Should().ContainSingle()
            .Which.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        repository.VerifyNoOtherCalls();
    }
}
