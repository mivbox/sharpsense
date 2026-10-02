using FluentResults;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace SharpSense.Infrastructure.CommandExecution;

internal sealed class SystemCommandRunner(CommandProcessHost host) : ICommandProcessRunner
{
    public async Task<Result<CommandProcessResult>> Execute(
        CommandProcessRequest request,
        Func<string, CancellationToken, Task> onOutput,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onOutput);

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return Result.Fail<CommandProcessResult>("Command must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            return Result.Fail<CommandProcessResult>("Working directory must not be empty.");
        }

        if (request.MaxCapturedLines <= 0 || request.MaxCapturedBytes <= 0)
        {
            return Result.Fail<CommandProcessResult>("Output capture limits must be greater than zero.");
        }

        var parsedCommand = CommandInvocationParser.Parse(request.Command);
        if (parsedCommand.IsFailed)
        {
            return Result.Fail<CommandProcessResult>(parsedCommand.Errors);
        }

        ct.ThrowIfCancellationRequested();
        var pipeName = "ss-" + Guid.NewGuid().ToString("N");
        using var control = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var process = new Process
        {
            StartInfo = CreateStartInfo(parsedCommand.Value, request.WorkingDirectory, pipeName)
        };

        try
        {
            if (!process.Start())
            {
                return Result.Fail<CommandProcessResult>($"Failed to start command '{request.Command}'.");
            }

            process.StandardInput.Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or FileNotFoundException or DirectoryNotFoundException)
        {
            return Result.Fail<CommandProcessResult>($"Failed to start command '{request.Command}': {ex.Message}");
        }

        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var cancellationRegistration = executionCancellation.Token.Register(control.Dispose);
        using var capture = new BoundedCommandOutput(request.MaxCapturedLines, request.MaxCapturedBytes, onOutput);
        var standardOutputTask = capture.Read(process.StandardOutput, executionCancellation.Token);
        var standardErrorTask = capture.Read(process.StandardError, executionCancellation.Token);

        CancelOnFault(standardOutputTask, executionCancellation);
        CancelOnFault(standardErrorTask, executionCancellation);
        var completionTask = ReadCompletion(control, executionCancellation.Token);
        CancelOnFault(completionTask, executionCancellation);

        try
        {
            var waitForExitTask = process.WaitForExitAsync(executionCancellation.Token);
            await Task.WhenAll(
                standardOutputTask,
                standardErrorTask,
                completionTask,
                waitForExitTask);
            var completion = await completionTask;

            return completion.Error is { } error
                ? Result.Fail<CommandProcessResult>($"Failed to run command: {error}")
                : Result.Ok(new CommandProcessResult(completion.ExitCode, capture.TotalLines, capture.Truncated));
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (Exception ex)
        {
            var captureException = GetCaptureException(standardOutputTask, standardErrorTask) ?? ex;

            return Result.Fail<CommandProcessResult>($"Failed while capturing command output: {captureException.Message}");
        }
        finally
        {
            // EOF tells the supervisor to terminate its own group, including
            // descendants whose original parent has already exited.
            control.Dispose();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                TryKill(process);
                await process.WaitForExitAsync();
            }
        }
    }

    private static async Task<CommandProcessCompletion> ReadCompletion(
        NamedPipeServerStream control,
        CancellationToken ct)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startup.CancelAfter(TimeSpan.FromSeconds(15));
        // A fast supervisor may exit while its connection/result is still queued.
        // Accept that result rather than racing process-exit notification delivery.
        await control.WaitForConnectionAsync(startup.Token);
        using var reader = new StreamReader(control, leaveOpen: true);
        var line = await reader.ReadLineAsync(ct);

        return line is null
            ? throw new InvalidOperationException("Command supervisor exited without a result.")
            : JsonSerializer.Deserialize<CommandProcessCompletion>(line)
              ?? throw new InvalidOperationException("Command supervisor returned an invalid result.");
    }

    private ProcessStartInfo CreateStartInfo(
        ParsedCommand parsedCommand,
        string workingDirectory,
        string pipeName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = host.Executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in host.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add(CommandProcessSupervisor.Argument);
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add(workingDirectory);
        startInfo.ArgumentList.Add(parsedCommand.Executable);
        foreach (var argument in parsedCommand.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void CancelOnFault(
        Task task,
        CancellationTokenSource executionCancellation)
    {
        task.ContinueWith(
            static (_, state) =>
            {
                var cancellation = (CancellationTokenSource)state!;
                if (!cancellation.IsCancellationRequested)
                {
                    cancellation.Cancel();
                }
            },
            executionCancellation,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
        }
    }

    private static Exception? GetCaptureException(params Task[] tasks)
        => tasks
            .Where(static task => task.IsFaulted)
            .Select(static task => task.Exception?.GetBaseException())
            .FirstOrDefault(static exception => exception is not null);
}
