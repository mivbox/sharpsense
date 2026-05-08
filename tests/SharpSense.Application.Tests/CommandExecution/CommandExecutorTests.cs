using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.CommandExecution;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Shared.Options;

namespace SharpSense.Application.Tests.CommandExecution;

public sealed class CommandExecutorTests
{
    [Fact]
    public async Task WhenQueryMatchesOverlappingWindows_ThenItReturnsMergedBlocks()
    {
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executionLogIndexFactory = new Mock<IExecutionLogIndexFactory>(MockBehavior.Strict);
        var executionLogIndex = new Mock<IExecutionLogIndex>(MockBehavior.Strict);

        executionLogIndexFactory.Setup(candidate => candidate.Create(CancellationToken.None))
            .ReturnsAsync(executionLogIndex.Object);
        executionLogIndex.SetupSequence(candidate => candidate.AppendLine(It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(Result.Ok(1))
            .ReturnsAsync(Result.Ok(2))
            .ReturnsAsync(Result.Ok(3))
            .ReturnsAsync(Result.Ok(4))
            .ReturnsAsync(Result.Ok(5))
            .ReturnsAsync(Result.Ok(6));
        executionLogIndex.Setup(candidate => candidate.FindMatches("Error", CancellationToken.None))
            .ReturnsAsync(Result.Ok<int[]>([2, 5]));
        executionLogIndex.Setup(candidate => candidate.ReadRange(new ExecutionLineRange(1, 6), CancellationToken.None))
            .ReturnsAsync(Result.Ok<ExecutionLogLine[]>(
            [
                new ExecutionLogLine(1, "Starting build"),
                new ExecutionLogLine(2, "Error: first failure"),
                new ExecutionLogLine(3, "  at Example.Build()"),
                new ExecutionLogLine(4, "Retrying"),
                new ExecutionLogLine(5, "Error: second failure"),
                new ExecutionLogLine(6, "Build finished")
            ]));
        executionLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
                It.Is<CommandProcessRequest>(request =>
                    request.Command == "dotnet build SharpSense.sln" &&
                    request.WorkingDirectory == "/repo"),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                CancellationToken.None))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                foreach (var line in new[]
                         {
                             "Starting build",
                             "Error: first failure",
                             "  at Example.Build()",
                             "Retrying",
                             "Error: second failure",
                             "Build finished"
                         })
                {
                    await onOutput(line, innerCt);
                }

                return Result.Ok(new CommandProcessResult(1));
            });
        var executor = CreateExecutor(processRunner, executionLogIndexFactory);

        var result = await executor.Execute(
            new CommandExecutionRequest("dotnet build SharpSense.sln", "Error"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeFalse();
        result.Value.Status.Should().Be("failure");
        result.Value.TotalLines.Should().Be(6);
        result.Value.MatchedLineCount.Should().Be(2);
        result.Value.Blocks.Should().ContainSingle();
        result.Value.Blocks[0].StartLine.Should().Be(1);
        result.Value.Blocks[0].EndLine.Should().Be(6);
        result.Value.Blocks[0].Text.Should().Be(
            "1| Starting build" + Environment.NewLine +
            "2| Error: first failure" + Environment.NewLine +
            "3|   at Example.Build()" + Environment.NewLine +
            "4| Retrying" + Environment.NewLine +
            "5| Error: second failure" + Environment.NewLine +
            "6| Build finished");
    }

    [Fact]
    public async Task WhenQueryIsMissing_ThenItReturnsCompactSummaryOnly()
    {
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executionLogIndexFactory = new Mock<IExecutionLogIndexFactory>(MockBehavior.Strict);
        var executionLogIndex = new Mock<IExecutionLogIndex>(MockBehavior.Strict);

        executionLogIndexFactory.Setup(candidate => candidate.Create(CancellationToken.None))
            .ReturnsAsync(executionLogIndex.Object);
        executionLogIndex.Setup(candidate => candidate.AppendLine("Build succeeded in 1.0s", CancellationToken.None))
            .ReturnsAsync(Result.Ok(1));
        executionLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
                It.IsAny<CommandProcessRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                CancellationToken.None))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                await onOutput("Build succeeded in 1.0s", innerCt);
                return Result.Ok(new CommandProcessResult(0));
            });
        var executor = CreateExecutor(processRunner, executionLogIndexFactory);

        var result = await executor.Execute(
            new CommandExecutionRequest("dotnet build SharpSense.sln", null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.Blocks.Should().BeEmpty();
        result.Value.MatchedLineCount.Should().Be(0);
        result.Value.Truncated.Should().BeFalse();
        result.Value.Summary.Should().Contain("No query was provided.");
        executionLogIndex.Verify(candidate => candidate.FindMatches(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenReducedOutputExceedsMaxCharacters_ThenItTruncatesReturnedBlocks()
    {
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executionLogIndexFactory = new Mock<IExecutionLogIndexFactory>(MockBehavior.Strict);
        var executionLogIndex = new Mock<IExecutionLogIndex>(MockBehavior.Strict);

        executionLogIndexFactory.Setup(candidate => candidate.Create(CancellationToken.None))
            .ReturnsAsync(executionLogIndex.Object);
        executionLogIndex.Setup(candidate => candidate.AppendLine("VeryLongFailureLine", CancellationToken.None))
            .ReturnsAsync(Result.Ok(1));
        executionLogIndex.Setup(candidate => candidate.FindMatches("VeryLongFailureLine", CancellationToken.None))
            .ReturnsAsync(Result.Ok<int[]>([1]));
        executionLogIndex.Setup(candidate => candidate.ReadRange(new ExecutionLineRange(1, 1), CancellationToken.None))
            .ReturnsAsync(Result.Ok<ExecutionLogLine[]>(
            [
                new ExecutionLogLine(1, "VeryLongFailureLine")
            ]));
        executionLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
                It.IsAny<CommandProcessRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                CancellationToken.None))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                await onOutput("VeryLongFailureLine", innerCt);
                return Result.Ok(new CommandProcessResult(1));
            });
        var executor = CreateExecutor(processRunner, executionLogIndexFactory);

        var result = await executor.Execute(
            new CommandExecutionRequest(
                "dotnet build SharpSense.sln",
                "VeryLongFailureLine",
                MaxCharacters: 12),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Truncated.Should().BeTrue();
        result.Value.Blocks.Should().ContainSingle();
        result.Value.Blocks[0].Text.Should().Be("1| VeryLo...");
        result.Value.Summary.Should().Contain("truncated at 12 characters");
    }

    [Fact]
    public async Task WhenProcessRunnerFails_ThenItReturnsFailureResult()
    {
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executionLogIndexFactory = new Mock<IExecutionLogIndexFactory>(MockBehavior.Strict);
        var executionLogIndex = new Mock<IExecutionLogIndex>(MockBehavior.Strict);

        executionLogIndexFactory.Setup(candidate => candidate.Create(CancellationToken.None))
            .ReturnsAsync(executionLogIndex.Object);
        executionLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
                It.IsAny<CommandProcessRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                CancellationToken.None))
            .ReturnsAsync(Result.Fail<CommandProcessResult>("Failed to start command 'missing-command'."));
        var executor = CreateExecutor(processRunner, executionLogIndexFactory);

        var result = await executor.Execute(
            new CommandExecutionRequest("missing-command", "Error"),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Contain("Failed to start command");
    }

    [Fact]
    public async Task WhenCapturedLinesExceedTheLimit_ThenItStopsIndexingAndFlagsTruncation()
    {
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executionLogIndexFactory = new Mock<IExecutionLogIndexFactory>(MockBehavior.Strict);
        var executionLogIndex = new Mock<IExecutionLogIndex>(MockBehavior.Strict);

        executionLogIndexFactory.Setup(candidate => candidate.Create(CancellationToken.None))
            .ReturnsAsync(executionLogIndex.Object);
        executionLogIndex.SetupSequence(candidate => candidate.AppendLine(It.IsAny<string>(), CancellationToken.None))
            .ReturnsAsync(Result.Ok(1))
            .ReturnsAsync(Result.Ok(2));
        executionLogIndex.Setup(candidate => candidate.FindMatches("Error", CancellationToken.None))
            .ReturnsAsync(Result.Ok<int[]>([2]));
        executionLogIndex.Setup(candidate => candidate.ReadRange(new ExecutionLineRange(1, 2), CancellationToken.None))
            .ReturnsAsync(Result.Ok<ExecutionLogLine[]>(
            [
                new ExecutionLogLine(1, "Starting build"),
                new ExecutionLogLine(2, "Error: first failure")
            ]));
        executionLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
                It.IsAny<CommandProcessRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                CancellationToken.None))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                foreach (var line in new[]
                         {
                             "Starting build",
                             "Error: first failure",
                             "Unbounded extra output 1",
                             "Unbounded extra output 2"
                         })
                {
                    await onOutput(line, innerCt);
                }

                return Result.Ok(new CommandProcessResult(1));
            });
        var executor = CreateExecutor(processRunner, executionLogIndexFactory);

        var result = await executor.Execute(
            new CommandExecutionRequest(
                "dotnet build SharpSense.sln",
                "Error",
                MaxCapturedLines: 2),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalLines.Should().Be(4);
        result.Value.Truncated.Should().BeTrue();
        result.Value.Blocks.Should().ContainSingle();
        result.Value.Blocks[0].Text.Should().Be(
            "1| Starting build" + Environment.NewLine +
            "2| Error: first failure");
        result.Value.Summary.Should().Contain("Output capture was capped at 2 indexed line(s).");
    }

    private static CommandExecutor CreateExecutor(
        Mock<ICommandProcessRunner> processRunner,
        Mock<IExecutionLogIndexFactory> executionLogIndexFactory)
        => new(
            processRunner.Object,
            executionLogIndexFactory.Object,
            Options.Create(new SharpSenseCliOptions
            {
                RepositoryRoot = "/repo"
            }));
}
