using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Infrastructure.Persistence.Records;

public sealed class DependencyEdgeRecord
{
    public int CallerNodeId { get; set; }

    public int CalleeNodeId { get; set; }

    public EdgeType EdgeType { get; set; } = EdgeType.ProjectReference;
}
