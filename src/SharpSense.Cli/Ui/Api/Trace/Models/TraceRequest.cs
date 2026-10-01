namespace SharpSense.Cli.Ui.Api;

public sealed record TraceRequest(int NodeId, string Direction = "callee", int MaxDepth = 3);
