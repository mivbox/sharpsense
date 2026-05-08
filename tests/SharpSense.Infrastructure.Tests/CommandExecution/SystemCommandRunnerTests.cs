using AwesomeAssertions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Infrastructure.CommandExecution;

namespace SharpSense.Infrastructure.Tests.CommandExecution;

public sealed class SystemCommandRunnerTests
{
    [Fact]
    public async Task WhenRunningDotnetVersion_ThenItStreamsOutputAndReturnsExitCode()
    {
        var runner = new SystemCommandRunner();
        var outputLines = new List<string>();

        var result = await runner.Execute(
            new CommandProcessRequest("dotnet --version", AppContext.BaseDirectory),
            (line, _) =>
            {
                outputLines.Add(line);
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExitCode.Should().Be(0);
        outputLines.Should().NotBeEmpty();
        outputLines[0].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WhenOutputCallbackFails_ThenItReturnsTheOriginalCaptureError()
    {
        var runner = new SystemCommandRunner();

        var result = await runner.Execute(
            new CommandProcessRequest("dotnet --version", AppContext.BaseDirectory),
            (_, _) => throw new InvalidOperationException("boom"),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Contain("boom");
    }
}
