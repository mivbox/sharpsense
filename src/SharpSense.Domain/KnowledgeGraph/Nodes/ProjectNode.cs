namespace SharpSense.Domain.KnowledgeGraph.Nodes;

public class ProjectNode
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string RelativeFilePath { get; set; } = string.Empty;

    public string ContentHash { get; set; } = string.Empty;
}
