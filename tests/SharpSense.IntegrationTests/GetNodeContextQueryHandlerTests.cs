using AwesomeAssertions;
using Moq;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.GetNodeContext;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.IntegrationTests;

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

        result.Should().BeSameAs(expected);
        repository.Verify(candidate => candidate.GetNodeContext(42, 50, ct), Times.Once);
    }

    [Fact]
    public async Task WhenRepositoryReturnsNull_ThenItThrowsInvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        var repository = new Mock<IContextRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetNodeContext(42, 10, ct))
            .ReturnsAsync((Context360Result?)null);
        var handler = new GetNodeContextQueryHandler(repository.Object);

        var act = async () => await handler.Handle(
            new GetNodeContextQuery(
                42,
                10),
            ct);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No persisted node exists for id 42.");
        repository.Verify(candidate => candidate.GetNodeContext(42, 10, ct), Times.Once);
    }
}
