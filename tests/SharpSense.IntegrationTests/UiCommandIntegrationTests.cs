using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharpSense.IntegrationTests;

public sealed class UiCommandIntegrationTests
{
    [Fact]
    public async Task WhenUiCommandRuns_ThenEndpointsReturnSuccessAndLogsAreWritten()
    {
        var cliAssemblyPath = Path.Combine(AppContext.BaseDirectory, "SharpSense.Cli.dll");
        var repositoryRoot = GetRepositoryPath(string.Empty);
        var logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".SharpSense",
            "logs",
            "ui.log");
        var uiProcess = await StartUiProcessAsync(cliAssemblyPath, repositoryRoot);
        using var process = uiProcess.Process;
        var baseUrl = uiProcess.BaseUrl;

        var stopped = false;

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            using var graphResponse = await httpClient.GetAsync($"{baseUrl}/api/graph", TestContext.Current.CancellationToken);
            using var rootResponse = await httpClient.GetAsync($"{baseUrl}/", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, graphResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);

            await StopProcessAsync(process);
            stopped = true;

            var logContents = await WaitForLogContentsAsync(logFilePath, baseUrl);

            Assert.Contains($"Configuring UI application for {baseUrl}", logContents, StringComparison.Ordinal);
            Assert.Contains($"Request starting HTTP/1.1 GET {baseUrl}/api/graph", logContents, StringComparison.Ordinal);
            Assert.Contains("HTTP GET / responded 200", logContents, StringComparison.Ordinal);
        }
        finally
        {
            if (!stopped)
            {
                await StopProcessAsync(process);
            }
        }
    }

    private static async Task<UiProcessHandle> StartUiProcessAsync(string cliAssemblyPath, string repositoryRoot)
    {
        var failures = new List<string>();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = GetAvailablePort();
            var baseUrl = $"http://127.0.0.1:{port}";
            var output = new StringBuilder();
            var process = new Process
            {
                StartInfo = CreateStartInfo(cliAssemblyPath, repositoryRoot, baseUrl)
            };

            process.OutputDataReceived += (_, args) => AppendOutput(output, args.Data);
            process.ErrorDataReceived += (_, args) => AppendOutput(output, args.Data);

            Assert.True(process.Start());
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await WaitForServerAsync(process, $"{baseUrl}/", output);
                return new UiProcessHandle(process, baseUrl);
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                failures.Add(ex.Message);
                await StopProcessAsync(process);
                process.Dispose();
            }
        }

        throw new InvalidOperationException(
            $"Unable to start the UI command after multiple attempts:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static ProcessStartInfo CreateStartInfo(string cliAssemblyPath, string repositoryRoot, string baseUrl)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(cliAssemblyPath);
        startInfo.ArgumentList.Add("ui");
        startInfo.ArgumentList.Add("--url");
        startInfo.ArgumentList.Add(baseUrl);

        return startInfo;
    }

    private static void AppendOutput(StringBuilder output, string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        output.AppendLine(data);
    }

    private static async Task WaitForServerAsync(Process process, string url, StringBuilder output)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var startedAtUtc = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAtUtc < TimeSpan.FromSeconds(30))
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The UI command exited before it became ready at '{url}'. Exit code: {process.ExitCode}.{Environment.NewLine}{output}");
            }

            try
            {
                using var response = await httpClient.GetAsync(url, TestContext.Current.CancellationToken);
                if (response.IsSuccessStatusCode && !process.HasExited)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"The UI command did not become ready at '{url}'.{Environment.NewLine}{output}");
    }

    private static async Task<string> WaitForLogContentsAsync(string logFilePath, string baseUrl)
    {
        var startedAtUtc = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAtUtc < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(logFilePath))
            {
                var logContents = await File.ReadAllTextAsync(logFilePath, TestContext.Current.CancellationToken);
                if (logContents.Contains(baseUrl, StringComparison.Ordinal))
                {
                    return logContents;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        throw new FileNotFoundException($"The UI log file '{logFilePath}' did not contain the expected URL '{baseUrl}'.");
    }

    private static async Task StopProcessAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string GetRepositoryPath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));

    private sealed record UiProcessHandle(Process Process, string BaseUrl);
}
