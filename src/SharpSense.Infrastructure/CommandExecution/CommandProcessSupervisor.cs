using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace SharpSense.Infrastructure.CommandExecution;

/// <summary>Host entry point that owns a command's descendants independently of its direct child.</summary>
public static class CommandProcessSupervisor
{
    public const string Argument = "--internal-command-supervisor";

    public static async Task<int> Run(string pipeName, string workingDirectory, string executable, string[] arguments)
    {
        using var control = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await control.ConnectAsync(10_000);
        using var writer = new StreamWriter(control, leaveOpen: true) { AutoFlush = true };
        CommandProcessGroup? group = null;

        try
        {
            // Ownership precedes user code. Children inherit the group/job even if
            // their immediate launcher exits before the owner observes them.
            group = CommandProcessGroup.Create();
            var ownerDisconnected = control.ReadAsync(new byte[1]).AsTask();
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Command did not start.");
            process.StandardInput.Close();
            var completion = Task.WhenAll(
                process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput()),
                process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError()),
                process.WaitForExitAsync());

            if (await Task.WhenAny(completion, ownerDisconnected) == completion)
            {
                await completion;
                await writer.WriteLineAsync(JsonSerializer.Serialize(new CommandProcessCompletion(process.ExitCode)));
            }
        }
        catch (Exception exception)
        {
            // Keep protocol output separate from command output and bound failure messages.
            var error = exception.Message[..Math.Min(exception.Message.Length, 1024)];
            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new CommandProcessCompletion(1, error)));
            }
            catch (IOException)
            {
                // The owner has already disconnected; containment still needs cleanup.
            }
        }
        finally
        {
            group?.Terminate();
        }

        return 1;
    }
}
