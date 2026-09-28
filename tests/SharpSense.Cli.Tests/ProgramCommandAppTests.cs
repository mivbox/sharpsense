using AwesomeAssertions;
using Spectre.Console.Testing;

namespace SharpSense.Cli.Tests;

public sealed class ProgramCommandAppTests
{
    [Fact]
    public async Task WhenVersion_ThenReportsCliAssemblyVersionWithoutStartingCommandServices()
    {
        using var console = new TestConsole();
        var app = global::SharpSense.Cli.Program.CreateCommandApp(
            console,
            configureServices: _ => throw new InvalidOperationException("Version must not start workspace services."),
            enableFileLogging: false);

        var exitCode = await app.RunAsync(["--version"], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Trim().Should().MatchRegex(@"^\d+\.\d+\.\d+(?:[-+].+)?$");
    }

    [Fact]
    public async Task WhenMcp_ThenRequiresAnExplicitWorkspaceBeforeStartingCommandServices()
    {
        using var console = new TestConsole();
        var servicesStarted = false;
        var app = global::SharpSense.Cli.Program.CreateCommandApp(console, _ => servicesStarted = true, enableFileLogging: false);

        var exitCode = await app.RunAsync(["mcp"], TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        console.Output.Should().Contain("MCP requires --workspace");
        servicesStarted.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task WhenBlankWorkspaceOptions_ThenFailBeforeStartingAnyCommandServices(string selector)
    {
        string[][] commands =
        [
            ["analyze"],
            ["doctor"],
            ["workspace", "show"],
            ["search", "example"],
            ["context", "--node-id", "1"],
            ["trace", "1"],
            ["inheritors", "1"],
            ["memory", "list", "--node-id", "1"],
            ["execute", "unused"],
            ["ui"]
        ];

        foreach (var command in commands)
        {
            using var console = new TestConsole();
            var servicesStarted = false;
            var app = global::SharpSense.Cli.Program.CreateCommandApp(
                console,
                _ =>
            {
                servicesStarted = true;
                throw new InvalidOperationException("Invalid selectors must not start services.");
            },
                enableFileLogging: false);

            var exitCode = await app.RunAsync([.. command, "--workspace", selector], TestContext.Current.CancellationToken);

            exitCode.Should().NotBe(0);
            console.Output.Should().Contain("--workspace must contain a workspace name or ID");
            servicesStarted.Should().BeFalse();
        }
    }

    [Fact]
    public void WhenFileLoggingIsDisabledWithoutAdditionalOverrides_ThenExecutionContextPreservesTheFlag()
    {
        var executionContext = global::SharpSense.Cli.Program.CreateExecutionContext(
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
        var executionContext = global::SharpSense.Cli.Program.CreateExecutionContext(
            console: null,
            configureServices: null,
            enableFileLogging: true);

        executionContext.Should().BeNull();
    }
}
