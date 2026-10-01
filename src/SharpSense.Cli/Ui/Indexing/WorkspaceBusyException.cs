namespace SharpSense.Cli.Ui.Indexing;

internal sealed class WorkspaceBusyException(string message) : InvalidOperationException(message);
