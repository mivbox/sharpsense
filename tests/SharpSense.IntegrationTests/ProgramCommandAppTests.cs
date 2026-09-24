using AwesomeAssertions;
using System.Reflection;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class ProgramCommandAppTests
{
    [Fact]
    public async Task VersionReportsCliAssemblyVersionWithoutStartingCommandServices()
    {
        using var console = new TestConsole();
        var app = Cli.Program.CreateCommandApp(
            console,
            configureServices: _ => throw new InvalidOperationException("Version must not start workspace services."),
            enableFileLogging: false);
        var expected = typeof(Cli.Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        var exitCode = await app.RunAsync(["--version"], TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal(expected, console.Output.Trim());
    }

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
