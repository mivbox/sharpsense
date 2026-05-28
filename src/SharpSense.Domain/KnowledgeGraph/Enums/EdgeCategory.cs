namespace SharpSense.Domain.KnowledgeGraph.Enums;

[Flags]
public enum EdgeCategory
{
    None = 0,
    Structural = 1 << 0,
    Semantic = 1 << 1,
    All = Structural | Semantic
}
