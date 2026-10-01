namespace SharpSense.Cli.Ui.Api;

public sealed record ContextRequest(int NodeId, int MaxRelated = 10);
