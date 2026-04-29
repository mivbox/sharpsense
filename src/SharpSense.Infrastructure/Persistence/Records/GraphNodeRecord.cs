namespace SharpSense.Infrastructure.Persistence.Records;

public sealed class GraphNodeRecord
{
    public int Id { get; set; }

    public string CanonicalId { get; set; } = string.Empty;

    public GraphNodeKind Kind { get; set; } = GraphNodeKind.Code;
}
