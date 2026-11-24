using AwesomeAssertions;
using Spectre.Console.Testing;

namespace SharpSense.Cli.Tests;

public sealed class ProgramCommandAppTests
{
    [Fact]
    public async Task WhenVersion_ThenReportsCliAssemblyVersionWithoutStartingCommandServices()
    {
        using var console = new TestConsole();
        var app = Program.CreateCommandApp(
            console,
            configureServices: _ => throw new InvalidOperationException("Version must not start workspace services."),
            enableFileLogging: false);

        var exitCode = await app.RunAsync(["--version"], TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Trim().Should().MatchRegex(@"^\d+\.\d+\.\d+(?:[-+].+)?$");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task WhenBlankWorkspaceOptions_ThenFailBeforeStartingAnyCommandServices(string selector)
    {
        string[][] commands =
        [
            ["mcp"],
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
            var app = Program.CreateCommandApp(
                console,
                _ =>
                {
                    servicesStarted = true;
                    throw new InvalidOperationException("Invalid selectors must not start services.");
                },
                enableFileLogging: false);

            var exitCode = await app.RunAsync(
                [.. command, "--workspace", selector],
                TestContext.Current.CancellationToken);

            exitCode.Should().NotBe(0);
            console.Output.Should().Contain("--workspace must contain a workspace name or ID");
            servicesStarted.Should().BeFalse();
        }
    }

    [Fact]
    public void WhenFileLoggingIsDisabledWithoutAdditionalOverrides_ThenExecutionContextPreservesTheFlag()
    {
        var executionContext = Program.CreateExecutionContext(
            console: null,
            configureServices: null,
            enableFileLogging: false);

        executionContext.Should().NotBeNull();
        executionContext!.EnableFileLogging.Should().BeFalse();
        executionContext.Console.Should().BeNull();
        executionContext.ConfigureServices.Should().BeNull();
    }

    [Theory]
    [InlineData(new[] { "memory", "list", "--limit", "0" }, "--limit must be greater than zero")]
    [InlineData(new[] { "memory", "list", "--offset", "-1" }, "--offset must not be negative")]
    [InlineData(new[] { "search", "query", "--limit", "99" }, "--limit must be between 1 and 50")]
    [InlineData(new[] { "search", "query", "--node-type", "Unknown" }, "--node-type values must be valid node types")]
    [InlineData(new[] { "context", "--node-id", "1", "--limit", "99" }, "--limit must be between 1 and 50")]
    [InlineData(new[] { "context", "--node-id", "1", "--section", "Unknown" }, "--section values must be one of")]
    [InlineData(new[] { "trace", "1", "--max-depth", "11" }, "--max-depth must be between 1 and 10")]
    public async Task WhenCommandOptionsAreInvalid_ThenValidationFailsBeforeStartingServices(
        string[] args,
        string expectedError)
    {
        using var console = new TestConsole();
        var servicesStarted = false;
        var app = Program.CreateCommandApp(
            console,
            _ =>
            {
                servicesStarted = true;
                throw new InvalidOperationException("Invalid options must not start services.");
            },
            enableFileLogging: false);

        var exitCode = await app.RunAsync(args, TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        console.Output.Should().Contain(expectedError);
        servicesStarted.Should().BeFalse();
    }
}
