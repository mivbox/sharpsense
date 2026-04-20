namespace SharpSense.Cli.Shared;

internal static class OutputFormatterFactory
{
    private static readonly IOutputFormatter _jsonOutputFormatter = new JsonOutputFormatter();
    private static readonly IOutputFormatter _toonOutputFormatter = new ToonOutputFormatter();

    public static IOutputFormatter Create(bool useToonFormat)
        => useToonFormat
            ? _toonOutputFormatter
            : _jsonOutputFormatter;
}
