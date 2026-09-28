using FluentResults;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using System.ComponentModel;
using System.Diagnostics;

namespace SharpSense.Infrastructure.CommandExecution;

internal sealed class SystemCommandRunner : ICommandProcessRunner
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

        var parsedCommand = CommandInvocationParser.Parse(request.Command);
        if (parsedCommand.IsFailed)
        {
            return Result.Fail<CommandProcessResult>(parsedCommand.Errors);
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(parsedCommand.Value, request.WorkingDirectory)
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
        using var cancellationRegistration = executionCancellation.Token.Register(() => TryKill(process));
        using var gate = new SemaphoreSlim(1, 1);
        var standardOutputTask = ReadLines(
            process.StandardOutput,
            onOutput,
            gate,
            executionCancellation.Token);
        var standardErrorTask = ReadLines(
            process.StandardError,
            onOutput,
            gate,
            executionCancellation.Token);

        CancelOnFault(standardOutputTask, executionCancellation);
        CancelOnFault(standardErrorTask, executionCancellation);

        try
        {
            var waitForExitTask = process.WaitForExitAsync(executionCancellation.Token);
            await Task.WhenAll(
                standardOutputTask,
                standardErrorTask,
                waitForExitTask);

            return Result.Ok(new CommandProcessResult(process.ExitCode));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            var captureException = GetCaptureException(standardOutputTask, standardErrorTask) ?? ex;

            return Result.Fail<CommandProcessResult>($"Failed while capturing command output: {captureException.Message}");
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        ParsedCommand parsedCommand,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = parsedCommand.Executable,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in parsedCommand.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task ReadLines(
        StreamReader reader,
        Func<string, CancellationToken, Task> onOutput,
        SemaphoreSlim gate,
        CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            await gate.WaitAsync(ct);
            try
            {
                await onOutput(line, ct);
            }
            finally
            {
                gate.Release();
            }
        }
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
