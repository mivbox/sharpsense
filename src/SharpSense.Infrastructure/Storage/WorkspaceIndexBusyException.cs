namespace SharpSense.Infrastructure.Storage;

public sealed class WorkspaceIndexBusyException(string message) : InvalidOperationException(message);
