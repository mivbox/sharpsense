namespace SharpSense.Infrastructure.Persistence.Records;

internal sealed class IndexRunStateRecord
{
    public int Id { get; set; } = 1;

    public string GraphRevision { get; set; } = "initial";

    public string? LastSuccessfulIndexJson { get; set; }

    public string? LastAttemptJson { get; set; }
}
