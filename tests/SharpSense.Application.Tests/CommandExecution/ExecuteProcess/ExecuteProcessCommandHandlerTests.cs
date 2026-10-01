using AwesomeAssertions;
using FluentResults;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.ExecuteProcess;
using SharpSense.Application.CommandExecution.ExecuteProcess.Models;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Shared.Options;

namespace SharpSense.Application.Tests.CommandExecution.ExecuteProcess;

public sealed class ExecuteProcessCommandHandlerTests
{
    [Fact]
    public async Task WhenQueryMatchesOverlappingWindows_ThenItReturnsMergedBlocks()
    {
        var ct = TestContext.Current.CancellationToken;
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory
            .Setup(candidate => candidate.Create(ct))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex
            .SetupSequence(candidate => candidate.AppendLine(
                It.IsAny<string>(),
                ct))
            .ReturnsAsync(Result.Ok(1))
            .ReturnsAsync(Result.Ok(2))
            .ReturnsAsync(Result.Ok(3))
            .ReturnsAsync(Result.Ok(4))
            .ReturnsAsync(Result.Ok(5))
            .ReturnsAsync(Result.Ok(6));
        executeLogIndex
            .Setup(candidate => candidate.FindMatches("Error", ct))
            .ReturnsAsync(Result.Ok<int[]>([2, 5]));
        executeLogIndex
            .Setup(candidate => candidate.ReadRange(
                new ExecutionLineRange(1, 6),
                ct))
            .ReturnsAsync(Result.Ok<ExecutionLogLine[]>(
                [
                    new ExecutionLogLine(1, "Starting build"),
                    new ExecutionLogLine(2, "Error: first failure"),
                    new ExecutionLogLine(3, "  at Example.Build()"),
                    new ExecutionLogLine(4, "Retrying"),
                    new ExecutionLogLine(5, "Error: second failure"),
                    new ExecutionLogLine(6, "Build finished")
                ]));
        executeLogIndex
            .Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner
            .Setup(candidate => candidate.Execute(
                It.Is<CommandProcessRequest>(request =>
                    request.Command == "dotnet build SharpSense.sln" &&
                    request.WorkingDirectory == "/repo"),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                ct))
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

                return Result.Ok(new CommandProcessResult(1, 6));
            });

        var handler = new ExecuteProcessCommandHandler(
            processRunner.Object,
            executeLogIndexFactory.Object,
            Options.Create(new WorkspaceExecutionOptions
            {
                RepositoryRoot = "/repo"
            }));

        var result = await handler.Handle(
            new ExecuteProcessCommand("dotnet build SharpSense.sln", "Error"),
            ct);

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
        var ct = TestContext.Current.CancellationToken;
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory
            .Setup(candidate => candidate.Create(ct))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex
            .Setup(candidate => candidate.AppendLine("Build succeeded in 1.0s", ct))
            .ReturnsAsync(Result.Ok(1));
        executeLogIndex
            .Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner
            .Setup(candidate => candidate.Execute(
                It.IsAny<CommandProcessRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                ct))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                await onOutput("Build succeeded in 1.0s", innerCt);

                return Result.Ok(new CommandProcessResult(0, 1));
            });

        var handler = new ExecuteProcessCommandHandler(
            processRunner.Object,
            executeLogIndexFactory.Object,
            Options.Create(new WorkspaceExecutionOptions
            {
                RepositoryRoot = "/repo"
            }));

        var result = await handler.Handle(
            new ExecuteProcessCommand("dotnet build SharpSense.sln", null),
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Success.Should().BeTrue();
        result.Value.Blocks.Should().BeEmpty();
        result.Value.MatchedLineCount.Should().Be(0);
        result.Value.Truncated.Should().BeFalse();
        result.Value.Summary.Should().Contain("No query was provided.");
        executeLogIndex.Verify(
            candidate => candidate.FindMatches(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WhenReducedOutputExceedsMaxCharacters_ThenItTruncatesReturnedBlocks()
    {
        var ct = TestContext.Current.CancellationToken;
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory
            .Setup(candidate => candidate.Create(ct))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex
            .Setup(candidate => candidate.AppendLine("VeryLongFailureLine", ct))
            .ReturnsAsync(Result.Ok(1));
        executeLogIndex
            .Setup(candidate => candidate.FindMatches("VeryLongFailureLine", ct))
            .ReturnsAsync(Result.Ok<int[]>([1]));
        executeLogIndex
            .Setup(candidate => candidate.ReadRange(
                new ExecutionLineRange(1, 1),
                ct))
            .ReturnsAsync(Result.Ok<ExecutionLogLine[]>(
                [
                    new ExecutionLogLine(1, "VeryLongFailureLine")
                ]));
        executeLogIndex
            .Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner
            .Setup(candidate => candidate.Execute(
                It.IsAny<CommandProcessRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                ct))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                await onOutput("VeryLongFailureLine", innerCt);

                return Result.Ok(new CommandProcessResult(1, 1));
            });

        var handler = new ExecuteProcessCommandHandler(
            processRunner.Object,
            executeLogIndexFactory.Object,
            Options.Create(new WorkspaceExecutionOptions
            {
                RepositoryRoot = "/repo"
            }));

        var result = await handler.Handle(
            new ExecuteProcessCommand("dotnet build SharpSense.sln", "VeryLongFailureLine", MaxCharacters: 12),
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Truncated.Should().BeTrue();
        result.Value.Blocks.Should().ContainSingle();
        result.Value.Blocks[0].Text.Should().Be("1| VeryLo...");
        result.Value.Summary.Should().Contain("truncated at 12 characters");
    }

    [Fact]
    public async Task WhenProcessRunnerFails_ThenItReturnsFailureResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory
            .Setup(candidate => candidate.Create(ct))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex
            .Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner
            .Setup(candidate => candidate.Execute(
                It.IsAny<CommandProcessRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                ct))
            .ReturnsAsync(Result.Fail<CommandProcessResult>("Failed to start command 'missing-command'."));

        var handler = new ExecuteProcessCommandHandler(
            processRunner.Object,
            executeLogIndexFactory.Object,
            Options.Create(new WorkspaceExecutionOptions
            {
                RepositoryRoot = "/repo"
            }));

        var result = await handler.Handle(
            new ExecuteProcessCommand("missing-command", "Error"),
            ct);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle();
        result.Errors[0].Message.Should().Contain("Failed to start command");
    }

    [Fact]
    public async Task WhenCapturedLinesExceedTheLimit_ThenItStopsIndexingAndFlagsTruncation()
    {
        var ct = TestContext.Current.CancellationToken;
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory
            .Setup(candidate => candidate.Create(ct))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex
            .SetupSequence(candidate => candidate.AppendLine(
                It.IsAny<string>(),
                ct))
            .ReturnsAsync(Result.Ok(1))
            .ReturnsAsync(Result.Ok(2));
        executeLogIndex
            .Setup(candidate => candidate.FindMatches("Error", ct))
            .ReturnsAsync(Result.Ok<int[]>([2]));
        executeLogIndex
            .Setup(candidate => candidate.ReadRange(
                new ExecutionLineRange(1, 2),
                ct))
            .ReturnsAsync(Result.Ok<ExecutionLogLine[]>(
                [
                    new ExecutionLogLine(1, "Starting build"),
                    new ExecutionLogLine(2, "Error: first failure")
                ]));
        executeLogIndex
            .Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner
            .Setup(candidate => candidate.Execute(
                It.IsAny<CommandProcessRequest>(),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                ct))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                foreach (var line in new[] { "Starting build", "Error: first failure" })
                {
                    await onOutput(line, innerCt);
                }

                return Result.Ok(new CommandProcessResult(1, 4, CaptureTruncated: true));
            });

        var handler = new ExecuteProcessCommandHandler(
            processRunner.Object,
            executeLogIndexFactory.Object,
            Options.Create(new WorkspaceExecutionOptions
            {
                RepositoryRoot = "/repo"
            }));

        var result = await handler.Handle(
            new ExecuteProcessCommand("dotnet build SharpSense.sln", "Error", MaxCapturedLines: 2),
            ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalLines.Should().Be(4);
        result.Value.Truncated.Should().BeTrue();
        result.Value.Blocks.Should().ContainSingle();
        result.Value.Blocks[0].Text.Should().Be(
            "1| Starting build" + Environment.NewLine +
            "2| Error: first failure");
        result.Value.Summary.Should().Contain("Output capture was capped at 2 indexed line(s) or 1048576 UTF-8 bytes.");
    }
    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("retained")]
    public async Task WhenRunnerTruncatesBytes_ThenPreservesTruncationForEveryQueryOutcome(string? query)
    {
        var ct = TestContext.Current.CancellationToken;
        var runner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var factory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var index = new Mock<IExecuteLogIndex>(MockBehavior.Strict);
        factory
            .Setup(candidate => candidate.Create(ct))
            .ReturnsAsync(index.Object);
        index
            .Setup(candidate => candidate.AppendLine("retained", ct))
            .ReturnsAsync(Result.Ok(1));
        index
            .Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        if (query is not null)
        {
            index
                .Setup(candidate => candidate.FindMatches(query, ct))
                .ReturnsAsync(Result.Ok<int[]>(query == "retained" ? [1] : []));
        }

        if (query == "retained")
        {
            index
                .Setup(candidate => candidate.ReadRange(new ExecutionLineRange(1, 1), ct))
                .ReturnsAsync(Result.Ok<ExecutionLogLine[]>([new(1, "retained")]));
        }

        runner
            .Setup(candidate => candidate.Execute(
                It.Is<CommandProcessRequest>(request => request.MaxCapturedBytes == 8 && request.MaxCapturedLines == 5000),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                ct))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> output,
                CancellationToken token) =>
            {
                await output("retained", token);

                return Result.Ok(new CommandProcessResult(0, 3, CaptureTruncated: true));
            });
        var handler = new ExecuteProcessCommandHandler(
            runner.Object,
            factory.Object,
            Options.Create(new WorkspaceExecutionOptions()));

        var result = await handler.Handle(new ExecuteProcessCommand("example", query, MaxCapturedBytes: 8), ct);

        result.IsSuccess.Should().BeTrue();
        result.Value.Truncated.Should().BeTrue();
        result.Value.TotalLines.Should().Be(3);
        result.Value.Summary.Should().Contain("8 UTF-8 bytes");
        result.Value.MatchedLineCount.Should().Be(query == "retained" ? 1 : 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task WhenByteLimitIsInvalid_ThenRejectsBeforeCreatingExecutionResources(int limit)
    {
        var handler = new ExecuteProcessCommandHandler(
            new Mock<ICommandProcessRunner>(MockBehavior.Strict).Object,
            new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict).Object,
            Options.Create(new WorkspaceExecutionOptions()));

        var result = await handler.Handle(
            new ExecuteProcessCommand("example", null, MaxCapturedBytes: limit),
            TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("Max captured bytes");
    }
}
