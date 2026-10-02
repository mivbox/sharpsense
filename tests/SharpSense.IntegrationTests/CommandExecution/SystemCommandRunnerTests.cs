using AwesomeAssertions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Infrastructure.CommandExecution;
using System.Diagnostics;

namespace SharpSense.IntegrationTests.CommandExecution;

public sealed class SystemCommandRunnerTests(CommandProcessFixture fixture) : IClassFixture<CommandProcessFixture>
{
    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    public async Task WhenCommandCompletes_ThenPreservesItsOutputAndExitCode(int exitCode)
    {
        var lines = new List<string>();
        var runner = CreateRunner();

        var result = await runner.Execute(
            new(fixture.Command($"exit {exitCode}"), AppContext.BaseDirectory),
            (line, _) =>
            {
                lines.Add(line);

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));
        result.Value.ExitCode.Should().Be(exitCode);
        lines.Should().BeEquivalentTo("command output", "command error");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenLauncherExitsAndExecutionIsInterrupted_ThenTerminatesItsRemainingChild(bool captureFails)
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var parent = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var child = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = CreateRunner();
        var execution = runner.Execute(new(fixture.Command("parent"), AppContext.BaseDirectory), async (line, token) =>
        {
            if (line.StartsWith("parent:", StringComparison.Ordinal))
            {
                parent.TrySetResult(int.Parse(line[7..]));
            }
            if (line.StartsWith("ready:", StringComparison.Ordinal))
            {
                child.TrySetResult(int.Parse(line[6..]));
                await release.Task.WaitAsync(token);
                if (captureFails)
                {
                    throw new InvalidOperationException("capture failed");
                }
            }
        }, cancellation.Token);
        try
        {
            var parentId = await parent.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            var childId = await child.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            await WaitForExit(parentId, ct);
            IsRunning(childId).Should().BeTrue();
            execution.IsCompleted.Should().BeFalse();

            if (captureFails)
            {
                release.SetResult();
                var result = await execution.WaitAsync(TimeSpan.FromSeconds(15), ct);
                result.IsFailed.Should().BeTrue();
                result.Errors.Should().ContainSingle().Which.Message.Should().Contain("capture failed");
            }
            else
            {
                await cancellation.CancelAsync();
                var action = async () => await execution.WaitAsync(TimeSpan.FromSeconds(15), ct);
                await action.Should().ThrowAsync<OperationCanceledException>();
            }

            await WaitForExit(childId, ct);
        }
        finally
        {
            await cancellation.CancelAsync();
            release.TrySetResult();
            try
            {
                await execution;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }
    }

    [Fact]
    public async Task WhenOneConcurrentCommandIsCancelled_ThenTheOtherKeepsRunning()
    {
        var ct = TestContext.Current.CancellationToken;
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var secondCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var firstReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReady = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = CreateRunner();
        var first = Start(firstReady, firstCancellation.Token);
        var second = Start(secondReady, secondCancellation.Token);
        try
        {
            var firstId = await firstReady.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            var secondId = await secondReady.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);

            await firstCancellation.CancelAsync();
            var action = async () => await first.WaitAsync(TimeSpan.FromSeconds(15), ct);
            await action.Should().ThrowAsync<OperationCanceledException>();

            await WaitForExit(firstId, ct);
            IsRunning(secondId).Should().BeTrue();
            second.IsCompleted.Should().BeFalse();
        }
        finally
        {
            await firstCancellation.CancelAsync();
            await secondCancellation.CancelAsync();
            try
            {
                await Task.WhenAll(first, second);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Task Start(TaskCompletionSource<int> ready, CancellationToken token)
            => runner.Execute(new(fixture.Command("block"), AppContext.BaseDirectory), (line, _) =>
            {
                ready.TrySetResult(int.Parse(line[6..]));

                return Task.CompletedTask;
            }, token);
    }

    [Fact]
    public async Task WhenAlreadyCancelled_ThenDoesNotStartTheCommand()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();
        var marker = Path.Combine(Path.GetDirectoryName(fixture.Executable)!, Guid.NewGuid().ToString("N"));
        var runner = CreateRunner();

        var action = async () => await runner.Execute(
            new(fixture.Command($"marker \"{marker}\""), AppContext.BaseDirectory),
            (_, _) => Task.CompletedTask,
            cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(marker).Should().BeFalse();
    }

    [Fact]
    public async Task WhenRunningDotnetVersion_ThenItStreamsOutputAndReturnsExitCode()
    {
        var runner = CreateRunner();
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
    public async Task WhenByteLimitIsReached_ThenReturnsChildExitAndReportsTruncation()
    {
        var lines = new List<string>();
        var runner = CreateRunner();

        var result = await runner.Execute(
            new CommandProcessRequest("dotnet --version", AppContext.BaseDirectory, MaxCapturedBytes: 1),
            (line, _) =>
            {
                lines.Add(line);

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExitCode.Should().Be(0);
        result.Value.TotalLines.Should().BeGreaterThanOrEqualTo(1);
        result.Value.CaptureTruncated.Should().BeTrue();
        lines.Should().ContainSingle().Which.Length.Should().Be(1);
    }

    [Fact]
    public async Task WhenOutputCallbackFails_ThenItReturnsTheOriginalCaptureError()
    {
        var runner = CreateRunner();

        var result = await runner.Execute(
            new CommandProcessRequest("dotnet --version", AppContext.BaseDirectory),
            (_, _) => throw new InvalidOperationException("boom"),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Contain("boom");
    }

    private static SystemCommandRunner CreateRunner()
        => new(new CommandProcessHost("dotnet", [typeof(Cli.Program).Assembly.Location]));

    private static async Task WaitForExit(int processId, CancellationToken ct)
    {
        var timeout = Stopwatch.StartNew();
        while (IsRunning(processId) && timeout.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(20, ct);
        }

        IsRunning(processId).Should().BeFalse($"process {processId} must exit");
    }

    private static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);

            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

}
