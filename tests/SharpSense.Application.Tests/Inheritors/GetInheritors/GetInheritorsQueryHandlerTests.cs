using AwesomeAssertions;
using Moq;
using SharpSense.Application.Inheritors.Abstractions;
using SharpSense.Application.Inheritors.GetInheritors;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Tests.Inheritors.GetInheritors;

public sealed class GetInheritorsQueryHandlerTests
{
    [Fact]
    public async Task WhenHandleWithValidQuery_ThenInvokesInheritorFinder()
    {
        var query = new GetInheritorsQuery(42);
        CodeNodeResult[] expected =
        [
            new CodeNodeResult(
                7,
                "node-derived",
                "project-app",
                "Fixture.App.DerivedWidget",
                "DerivedWidget",
                NodeType.Class,
                "src/Fixture.App/DerivedWidget.cs",
                4,
                18,
                "Derived widget.")
        ];
        var inheritorFinder = new Mock<IInheritorFinder>(MockBehavior.Strict);
        inheritorFinder.Setup(candidate => candidate.GetInheritors(query, CancellationToken.None))
            .ReturnsAsync(expected);
        var handler = new GetInheritorsQueryHandler(inheritorFinder.Object);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().BeSameAs(expected);
        inheritorFinder.Verify(candidate => candidate.GetInheritors(query, CancellationToken.None), Times.Once);
    }
}
