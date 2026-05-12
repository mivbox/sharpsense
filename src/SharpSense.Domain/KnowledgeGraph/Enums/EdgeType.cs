namespace SharpSense.Domain.KnowledgeGraph.Enums;

public enum EdgeType
{
    ProjectReference,
    ParentOf,
    MethodCall,
    Implements,
    Instantiates,
    FieldAccess,
    ServiceRegistration,
    DocumentLink
}
