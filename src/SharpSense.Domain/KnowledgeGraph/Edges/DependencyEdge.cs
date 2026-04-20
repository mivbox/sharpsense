using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Domain.KnowledgeGraph.Edges;

public class DependencyEdge
{
    public string CallerId { get; set; } = string.Empty;

    public string CalleeId { get; set; } = string.Empty;

    public EdgeType EdgeType { get; set; } = EdgeType.ProjectReference;
}
