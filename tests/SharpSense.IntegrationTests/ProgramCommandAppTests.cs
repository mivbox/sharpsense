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
    public async Task McpRequiresAnExplicitWorkspaceBeforeStartingCommandServices()
    {
        using var console = new TestConsole();
        var servicesStarted = false;
        var app = Cli.Program.CreateCommandApp(console, _ => servicesStarted = true, enableFileLogging: false);

        var exitCode = await app.RunAsync(["mcp"], TestContext.Current.CancellationToken);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("MCP requires --workspace", console.Output);
        Assert.False(servicesStarted);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankWorkspaceOptionsFailBeforeStartingAnyCommandServices(string selector)
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
            var app = Cli.Program.CreateCommandApp(console, _ =>
            {
                servicesStarted = true;
                throw new InvalidOperationException("Invalid selectors must not start services.");
            }, enableFileLogging: false);

            var exitCode = await app.RunAsync([.. command, "--workspace", selector], TestContext.Current.CancellationToken);

            Assert.NotEqual(0, exitCode);
            Assert.Contains("--workspace must contain a workspace name or ID", console.Output);
            Assert.False(servicesStarted);
        }
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
