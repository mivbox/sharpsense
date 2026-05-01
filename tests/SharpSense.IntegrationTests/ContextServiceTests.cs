using AwesomeAssertions;
using Moq;
using SharpSense.Application.Context360;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.IntegrationTests;

public sealed class ContextServiceTests
{
    [Fact]
    public async Task WhenGetNodeContextHasRelatedMethods_ThenItSanitizesRelatedNodeNames()
    {
        var lookup = new Mock<IContextLookup>(MockBehavior.Strict);
        lookup.Setup(candidate => candidate.GetNodeContext(42, 10, CancellationToken.None))
            .ReturnsAsync(
                new Context360LookupResult(
                    new CodeNodeResult(
                        42,
                        "code:project-app:Fixture.App.PaymentProcessor.ProcessPayment(string, int)",
                        "project-app",
                        "Fixture.App.PaymentProcessor.ProcessPayment(string, int)",
                        "PaymentProcessor.ProcessPayment(string, int)",
                        NodeType.Method,
                        "src/Fixture.App/PaymentProcessor.cs",
                        12,
                        30,
                        "Processes a payment."),
                    [
                        new CodeNodeResult(
                            7,
                            "code:project-app:Fixture.App.HttpEndpoint.Handle(Guid)",
                            "project-app",
                            "Fixture.App.HttpEndpoint.Handle(Guid)",
                            "HttpEndpoint.Handle(Guid)",
                            NodeType.Method,
                            "src/Fixture.App/HttpEndpoint.cs",
                            5,
                            12,
                            "Handles an HTTP request.")
                    ],
                    [
                        new CodeNodeResult(
                            8,
                            "code:project-app:Fixture.App.PaymentProcessorBase",
                            "project-app",
                            "Fixture.App.PaymentProcessorBase",
                            "PaymentProcessorBase",
                            NodeType.Class,
                            "src/Fixture.App/PaymentProcessorBase.cs",
                            3,
                            20,
                            "Base processor.")
                    ],
                    [
                        new CodeNodeResult(
                            9,
                            "code:project-app:Fixture.App.ReceiptWriter.WriteReceipt(CancellationToken)",
                            "project-app",
                            "Fixture.App.ReceiptWriter.WriteReceipt(CancellationToken)",
                            "ReceiptWriter.WriteReceipt(CancellationToken)",
                            NodeType.Method,
                            "src/Fixture.App/ReceiptWriter.cs",
                            8,
                            16,
                            "Writes a receipt.")
                    ],
                    [
                        new CodeNodeResult(
                            10,
                            "code:project-app:Fixture.App.IPaymentProcessor",
                            "project-app",
                            "Fixture.App.IPaymentProcessor",
                            "IPaymentProcessor",
                            NodeType.Interface,
                            "src/Fixture.App/IPaymentProcessor.cs",
                            3,
                            9,
                            "Payment contract.")
                    ]));
        var service = new ContextService(lookup.Object);

        var result = await service.GetNodeContext(42, 10, TestContext.Current.CancellationToken);

        result.TargetNode.Name.Should().Be("PaymentProcessor.ProcessPayment(string, int)");
        result.Callers.Should().ContainSingle().Which.Name.Should().Be("HttpEndpoint.Handle");
        result.Implementers.Should().ContainSingle().Which.Name.Should().Be("PaymentProcessorBase");
        result.Callees.Should().ContainSingle().Which.Name.Should().Be("ReceiptWriter.WriteReceipt");
        result.Inherits.Should().ContainSingle().Which.Name.Should().Be("IPaymentProcessor");
        lookup.Verify(candidate => candidate.GetNodeContext(42, 10, CancellationToken.None), Times.Once);
    }
}
