namespace SharpSense.Domain.KnowledgeGraph.Enums;

public static class KnowledgeGraphEdgeTypes
{
    public static EdgeType[] All { get; } = Enum.GetValues<EdgeType>();

    public static EdgeType[] Functional { get; } =
        [.. All.Where(static edgeType => edgeType != EdgeType.ParentOf)];
}
