namespace SharpSense.Domain.KnowledgeGraph.Enums;

public enum EdgeType
{
    ProjectReference,
    MethodCall,
    Implements,
    Instantiates,
    FieldAccess,
    ServiceRegistration,
    DocumentLink,
    DocumentHierarchy
}
