using AwesomeAssertions;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

public sealed class MsBuildDiagnosticLogTests
{
    [Fact]
    public async Task WhenBinlogHasNewerForwardCompatibleFormatVersion_ThenReadReplaysWarningsAndErrors()
    {
        var ct = TestContext.Current.CancellationToken;
        MsBuildLocatorRegistration.EnsureRegistered();
        var tempDirectory = Directory.CreateTempSubdirectory("sharpsense-binlog-test-");

        try
        {
            var projectPath = Path.Combine(tempDirectory.FullName, "Fixture.proj");
            await File.WriteAllTextAsync(
                projectPath,
                """
                <Project DefaultTargets="EmitDiagnostics">
                  <Target Name="EmitDiagnostics">
                    <Warning Code="SSWARN1" Text="Forward-compatible warning" />
                    <Error Code="SSERR1" Text="Forward-compatible error" />
                  </Target>
                </Project>
                """,
                ct);

            using var log = new MsBuildDiagnosticLog();
            var binlogPath = Path.Combine(log.DirectoryPath, "workspace.binlog");
            await GenerateBinlog(projectPath, binlogPath, ct);
            RewriteBinlogVersionHeader(binlogPath, fileFormatVersion: 28, minimumReaderVersion: 18);

            var report = log.Read(ct);

            report.Warnings.Should().ContainSingle(warning =>
                warning.Code == "SSWARN1" &&
                warning.Message == "Forward-compatible warning");
            report.Errors.Should().ContainSingle(error =>
                error.Code == "SSERR1" &&
                error.Message == "Forward-compatible error");
        }
        finally
        {
            if (tempDirectory.Exists)
            {
                tempDirectory.Delete(recursive: true);
            }
        }
    }

    private static async Task GenerateBinlog(string projectPath, string binlogPath, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add($"-bl:{binlogPath};ProjectImports=None");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet msbuild.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);

        File.Exists(binlogPath).Should().BeTrue("dotnet msbuild should emit the diagnostic binlog");
    }

    private static void RewriteBinlogVersionHeader(string binlogPath, int fileFormatVersion, int minimumReaderVersion)
    {
        byte[] payload;
        using (var input = File.OpenRead(binlogPath))
        using (var gzip = new GZipStream(input, CompressionMode.Decompress))
        using (var buffer = new MemoryStream())
        {
            gzip.CopyTo(buffer);
            payload = buffer.ToArray();
        }

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, sizeof(int)), fileFormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int), sizeof(int)), minimumReaderVersion);

        using var output = File.Create(binlogPath);
        using var recompressed = new GZipStream(output, CompressionLevel.Fastest);
        recompressed.Write(payload);
    }
}
