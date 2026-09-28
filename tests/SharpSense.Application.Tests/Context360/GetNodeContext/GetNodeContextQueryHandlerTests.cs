using AwesomeAssertions;
using Moq;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.GetNodeContext;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Errors;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Context360.GetNodeContext;

public sealed class GetNodeContextQueryHandlerTests
{
    [Fact]
    public async Task WhenMaxRelatedExceedsLimit_ThenItClampsAndReturnsRepositoryResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var expected = new Context360Result(
            new Context360Node(
                42,
                "PaymentProcessor.ProcessPayment(string, int)",
                NodeType.Method,
                "src/Fixture.App/PaymentProcessor.cs",
                12,
                30),
            [new Context360RelatedNode(7, "HttpEndpoint.Handle")],
            [new Context360RelatedNode(8, "PaymentProcessorBase")],
            [new Context360RelatedNode(9, "ReceiptWriter.WriteReceipt")],
            [new Context360RelatedNode(10, "IPaymentProcessor")],
            [new Context360RelatedNode(11, "PaymentProcessor")],
            []);
        var repository = new Mock<IContextRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetNodeContext(42, 50, ct))
            .ReturnsAsync(expected);
        var handler = new GetNodeContextQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetNodeContextQuery(
                42,
                100),
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected);
        repository.Verify(candidate => candidate.GetNodeContext(42, 50, ct), Times.Once);
    }

    [Fact]
    public async Task WhenRepositoryReturnsNull_ThenItReturnsNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IContextRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetNodeContext(42, 10, ct))
            .ReturnsAsync((Context360Result?)null);
        var handler = new GetNodeContextQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetNodeContextQuery(
                42,
                10),
            ct);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors
            .OfType<ServiceError>()
            .Should().ContainSingle().Which;
        error.ErrorCode.Should().Be(ServiceErrorCode.NotFound);
        error.Message.Should().Be("No persisted node exists for id 42.");
        repository.Verify(candidate => candidate.GetNodeContext(42, 10, ct), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task WhenNodeIdIsNotPositive_ThenItReturnsInvalidArgumentWithoutReadingTheRepository(int nodeId)
    {
        var repository = new Mock<IContextRepository>(MockBehavior.Strict);
        var handler = new GetNodeContextQueryHandler(repository.Object);

        var result = await handler.Handle(new GetNodeContextQuery(nodeId, 10), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors
            .OfType<ServiceError>()
            .Should().ContainSingle().Which;
        error.ErrorCode.Should().Be(ServiceErrorCode.InvalidArgument);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenRepositoryCancels_ThenCancellationPropagates()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IContextRepository>(MockBehavior.Strict);
        repository
            .Setup(candidate => candidate.GetNodeContext(42, 10, ct))
            .ThrowsAsync(new OperationCanceledException(ct));
        var handler = new GetNodeContextQueryHandler(repository.Object);

        var act = () => handler.Handle(new GetNodeContextQuery(42, 10), ct);

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
    }
}
