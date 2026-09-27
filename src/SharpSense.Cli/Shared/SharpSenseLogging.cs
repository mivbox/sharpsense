using Serilog;
using Serilog.Events;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.Text;

namespace SharpSense.Cli.Shared;

internal static class SharpSenseLogging
{
    private const string _defaultLogFileName = "sharp-sense";
    private static readonly HashSet<char> _invalidFileNameCharacters = [.. Path.GetInvalidFileNameChars()];

    public static ILogger CreateLogger(
        bool isVerbose,
        string? commandName,
        bool enableConsoleLogging,
        bool enableFileLogging = true)
    {
        var configuration = new LoggerConfiguration();
        ConfigureLogger(
            configuration,
            isVerbose,
            enableFileLogging ? GetLogFilePath(commandName) : null,
            enableConsoleLogging,
            enableFileLogging);

        return configuration.CreateLogger();
    }

    public static void UseGlobalLogger(
        bool isVerbose,
        string? commandName,
        bool enableConsoleLogging,
        bool enableFileLogging = true)
        => Log.Logger = CreateLogger(isVerbose, commandName, enableConsoleLogging, enableFileLogging);

    public static string GetLogFilePath(string? commandName)
    {
        var loggingDirectory = Path.Combine(
            SharpSenseHome.Resolve(new FileSystem()),
            "logs");

        var safeCommandName = CreateSafeFileNameSegment(commandName);

        return Path.Combine(
            loggingDirectory,
            $"{safeCommandName}.log");
    }

    public static void ConfigureLogger(
        LoggerConfiguration configuration,
        bool isVerbose,
        string? logFilePath,
        bool enableConsoleLogging,
        bool enableFileLogging = true)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration
            .MinimumLevel.Is(isVerbose ? LogEventLevel.Verbose : LogEventLevel.Information).Enrich.FromLogContext();

        if (enableFileLogging)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
            configuration.WriteTo.File(
                path: logFilePath,
                fileSizeLimitBytes: 5_242_880L,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 10,
                shared: true,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {SourceContext}{NewLine}{Exception}");
        }

        if (enableConsoleLogging)
        {
            configuration.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);
        }
    }

    private static string CreateSafeFileNameSegment(string? value)
    {
        var normalizedValue = string.IsNullOrWhiteSpace(value)
            ? _defaultLogFileName
            : value.Trim()
                .ToLowerInvariant();

        var builder = new StringBuilder(normalizedValue.Length);
        foreach (var character in normalizedValue)
        {
            builder.Append(_invalidFileNameCharacters.Contains(character) || char.IsWhiteSpace(character) ? '-' : character);
        }

        return builder.Length == 0
            ? _defaultLogFileName
            : builder.ToString();
    }
}
