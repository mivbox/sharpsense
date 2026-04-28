using AwesomeAssertions;

namespace SharpSense.IntegrationTests;

public sealed class ProgramCommandAppTests
{
    [Fact]
    public void WhenFileLoggingIsDisabledWithoutAdditionalOverrides_ThenExecutionContextPreservesTheFlag()
    {
        var executionContext = Cli.Program.CreateExecutionContext(
            console: null,
            configureServices: null,
            enableFileLogging: false);

        executionContext.Should().NotBeNull();
        executionContext!.EnableFileLogging.Should().BeFalse();
        executionContext.Console.Should().BeNull();
        executionContext.ConfigureServices.Should().BeNull();
    }

    [Fact]
    public void WhenDefaultSettingsHaveNoOverrides_ThenExecutionContextIsOmitted()
    {
        var executionContext = Cli.Program.CreateExecutionContext(
            console: null,
            configureServices: null,
            enableFileLogging: true);

        executionContext.Should().BeNull();
    }
}
