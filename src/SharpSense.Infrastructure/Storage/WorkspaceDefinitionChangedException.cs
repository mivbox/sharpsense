namespace SharpSense.Infrastructure.Storage;

public sealed class WorkspaceDefinitionChangedException(string message) : InvalidOperationException(message);
