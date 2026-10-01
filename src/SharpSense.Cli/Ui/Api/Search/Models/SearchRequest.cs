namespace SharpSense.Cli.Ui.Api;

public sealed record SearchRequest(string Query, int Limit = 10);
