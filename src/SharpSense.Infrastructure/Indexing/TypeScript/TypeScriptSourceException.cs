namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class TypeScriptSourceException(string filePath, string message, Exception innerException)
    : Exception(message, innerException)
{
    public string FilePath { get; } = filePath;
}
