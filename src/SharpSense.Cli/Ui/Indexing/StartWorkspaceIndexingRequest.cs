namespace SharpSense.Cli.Ui.Indexing;

public sealed record StartWorkspaceIndexingRequest(bool Watch = false, bool SkipEmbeddings = false);
