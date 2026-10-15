using System.Diagnostics;

namespace SharpSense.Testkit;

public static class TestProcess
{
    public static async Task<(int ExitCode, string Output, string Error)> Run(
        ProcessStartInfo start,
        CancellationToken ct)
    {
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        // Reused MSBuild workers can retain redirected pipes after the command exits.
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start test process.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            return (process.ExitCode, await output, await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                // Cancellation must still allow a bounded wait to reap the owned process.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await process.WaitForExitAsync(cleanup.Token);
            }
        }
    }
}
