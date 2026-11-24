using Microsoft.Build.Logging;
using Serilog;
using System.Runtime.CompilerServices;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

/// <summary>
/// Recovers original MSBuild severity because Roslyn reports both build warnings and
/// errors as WorkspaceDiagnosticKind.Failure. Unmatched workspace failures stay fatal.
/// </summary>
internal sealed class MsBuildDiagnosticLog : IDisposable
{
    private static readonly Serilog.ILogger _logger = Log.ForContext<MsBuildDiagnosticLog>();
    private readonly DirectoryInfo _directory;

    // Construct only after the workspace factory registers the installed MSBuild SDK.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public MsBuildDiagnosticLog()
    {
        _directory = Directory.CreateTempSubdirectory("sharpsense-msbuild-");
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    _directory.FullName,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Logger = new BinaryLogger
            {
                Parameters = Path.Combine(_directory.FullName, "workspace.binlog"),
                CollectProjectImports = BinaryLogger.ProjectImportsCollectionMode.None
            };
            // Roslyn forwards only the filename to its external build host, which may
            // embed imports despite this setting. Keep all logs private and ephemeral.
        }
        catch
        {
            _directory.Delete(recursive: true);
            throw;
        }
    }

    public Microsoft.Build.Framework.ILogger Logger { get; }

    internal string DirectoryPath => _directory.FullName;

    public MsBuildDiagnosticReport Read(CancellationToken ct)
    {
        var warnings = new List<MsBuildMessage>();
        var errors = new List<MsBuildMessage>();
        foreach (var path in Directory.EnumerateFiles(_directory.FullName, "*.binlog"))
        {
            ct.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(path);
            // Roslyn's external build host may run a newer installed SDK (writing a higher
            // binlog format version) than the MSBuild version registered in this process.
            var events = new BinaryLogReplayEventSource
            {
                AllowForwardCompatibility = true
            };
            events.RecoverableReadError += readError => _logger.Debug(
                "Skipped forward-compatible MSBuild binlog record ({ErrorType}/{RecordKind}): {Message}",
                readError.ErrorType,
                readError.RecordKind,
                readError.GetFormattedMessage());
            events.WarningRaised += (_, warning) => warnings.Add(new MsBuildMessage(
                warning.ProjectFile,
                warning.Code,
                warning.Message));
            events.ErrorRaised += (_, error) => errors.Add(new MsBuildMessage(
                error.ProjectFile,
                error.Code,
                error.Message));
            events.Replay(stream, ct);
        }

        return new MsBuildDiagnosticReport(
            warnings
                .Distinct()
                .ToArray(),
            errors
                .Distinct()
                .ToArray());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory.FullName))
        {
            _directory.Delete(recursive: true);
        }
    }
}
