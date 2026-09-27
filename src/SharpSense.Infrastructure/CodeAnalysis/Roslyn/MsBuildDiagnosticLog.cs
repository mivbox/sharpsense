using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

/// <summary>
/// Recovers original MSBuild severity because Roslyn reports both build warnings and
/// errors as WorkspaceDiagnosticKind.Failure. Unmatched workspace failures stay fatal.
/// </summary>
internal sealed class MsBuildDiagnosticLog : IDisposable
{
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

    public ILogger Logger
    {
        get;
    }

    internal string DirectoryPath => _directory.FullName;

    public MsBuildDiagnosticReport Read(CancellationToken ct)
    {
        var warnings = new List<MsBuildMessage>();
        var errors = new List<MsBuildMessage>();
        foreach (var path in Directory.EnumerateFiles(_directory.FullName, "*.binlog"))
        {
            ct.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(path);
            var events = new BinaryLogReplayEventSource();
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
            warnings.Distinct()
                .ToArray(),
            errors.Distinct()
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

internal sealed record MsBuildDiagnosticReport(
    IReadOnlyList<MsBuildMessage> Warnings,
    IReadOnlyList<MsBuildMessage> Errors)
{
    public bool IsConfirmedWarning(WorkspaceDiagnostic diagnostic)
        => diagnostic.Kind == WorkspaceDiagnosticKind.Failure &&
           Warnings.Any(warning => warning.Matches(diagnostic)) &&
           !Errors.Any(error => error.Matches(diagnostic));
}

internal sealed record MsBuildMessage(string? ProjectFile, string? Code, string? Message)
{
    public bool Matches(WorkspaceDiagnostic diagnostic)
    {
        // Match an observed typed event's complete message and owning project. Do not
        // infer severity from English words, diagnostic codes, or an empty error list.
        if (string.IsNullOrWhiteSpace(ProjectFile) || string.IsNullOrWhiteSpace(Message))
        {
            return false;
        }

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return diagnostic.Message.Contains(
            $"'{ProjectFile}'",
            pathComparison) &&
            diagnostic.Message.EndsWith(
                $": {Message}",
                StringComparison.Ordinal);
    }

    public string Format(string severity) => $"MSBuild {severity} {Code} in '{ProjectFile}': {Message}";
}
