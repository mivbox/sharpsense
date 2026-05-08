using AwesomeAssertions;
using SharpSense.Infrastructure.CommandExecution;

namespace SharpSense.Infrastructure.Tests.CommandExecution;

public sealed class CommandInvocationParserTests
{
    [Fact]
    public void WhenCommandContainsQuotedArguments_ThenItPreservesInnerSpaces()
    {
        var result = CommandInvocationParser.Parse("dotnet test \"tests/SharpSense.IntegrationTests/SharpSense.IntegrationTests.csproj\" --filter \"FullyQualifiedName~Cli Command\"");

        result.IsSuccess.Should().BeTrue();
        result.Value.Executable.Should().Be("dotnet");
        result.Value.Arguments.Should().Equal(
            "test",
            "tests/SharpSense.IntegrationTests/SharpSense.IntegrationTests.csproj",
            "--filter",
            "FullyQualifiedName~Cli Command");
    }
}
