namespace SharpSense.Infrastructure.DependencyGraph;

internal static class GraphPageSql
{
    public const string Scope = """
        WITH ScopeDirectories AS (
            SELECT DISTINCT DescendantDirectoryId AS Id FROM DirectoryClosures
            WHERE AncestorDirectoryId IN (SELECT value FROM json_each($directories))
        )
        """;

    public const string SelectedNodes = """
        , SelectedNodes AS MATERIALIZED (
            SELECT c.Id FROM CodeNodes c JOIN Documents d ON d.Id = c.DocumentId
            WHERE d.DirectoryId IN (SELECT Id FROM ScopeDirectories)
            UNION
            SELECT p.Id FROM ProjectNodes p JOIN Documents d ON d.Id = p.ProjectDocumentId
            WHERE d.DirectoryId IN (SELECT Id FROM ScopeDirectories)
        )
        """;

    public const string BoundaryNodes = """
        , BoundaryNodes AS (
            SELECT e.CalleeNodeId AS Id FROM DependencyEdges e
            JOIN SelectedNodes s ON s.Id = e.CallerNodeId
            UNION
            SELECT e.CallerNodeId AS Id FROM DependencyEdges e
            JOIN SelectedNodes s ON s.Id = e.CalleeNodeId
            EXCEPT SELECT Id FROM SelectedNodes
        )
        """;

    public const string VisibleEdges = """
        , VisibleEdges AS (
            SELECT e.CallerNodeId, e.CalleeNodeId, e.EdgeType FROM DependencyEdges e
            JOIN SelectedNodes s ON s.Id = e.CallerNodeId
            UNION
            SELECT e.CallerNodeId, e.CalleeNodeId, e.EdgeType FROM DependencyEdges e
            JOIN SelectedNodes s ON s.Id = e.CalleeNodeId
        )
        """;

    public const string NodeColumns = """
        g.Id,
        COALESCE(c.FullyQualifiedName, p.Name, g.CanonicalId) AS Label,
        CASE WHEN c.Id IS NOT NULL THEN lower(c.NodeType)
             WHEN p.Id IS NOT NULL THEN 'project' ELSE lower(g.Kind) END AS Type,
        d.RelativePath,
        CASE WHEN p.Id IS NOT NULL THEN p.Id ELSE c.ProjectNodeId END AS ProjectId,
        c.Id AS CodeNodeId
        """;

    public const string NodeJoins = """
        FROM GraphNodes g
        LEFT JOIN CodeNodes c ON c.Id = g.Id
        LEFT JOIN ProjectNodes p ON p.Id = g.Id
        LEFT JOIN Documents d ON d.Id = COALESCE(c.DocumentId, p.ProjectDocumentId)
        """;

    public const string ValidNode = "(c.Id IS NOT NULL OR p.Id IS NOT NULL OR g.Kind IN ('Http', 'Package'))";
    public const string SelectedNode = "d.DirectoryId IN (SELECT Id FROM ScopeDirectories)";

    public const string EdgeJoins = """
        FROM DependencyEdges e
        JOIN GraphNodes cg ON cg.Id = e.CallerNodeId
        JOIN GraphNodes tg ON tg.Id = e.CalleeNodeId
        LEFT JOIN CodeNodes cc ON cc.Id = e.CallerNodeId
        LEFT JOIN ProjectNodes cp ON cp.Id = e.CallerNodeId
        LEFT JOIN Documents cd ON cd.Id = COALESCE(cc.DocumentId, cp.ProjectDocumentId)
        LEFT JOIN CodeNodes tc ON tc.Id = e.CalleeNodeId
        LEFT JOIN ProjectNodes tp ON tp.Id = e.CalleeNodeId
        LEFT JOIN Documents td ON td.Id = COALESCE(tc.DocumentId, tp.ProjectDocumentId)
        """;

    public const string SelectedCaller = "cd.DirectoryId IN (SELECT Id FROM ScopeDirectories)";
    public const string SelectedCallee = "td.DirectoryId IN (SELECT Id FROM ScopeDirectories)";
    public const string ValidEdges = """
        (cc.Id IS NOT NULL OR cp.Id IS NOT NULL OR cg.Kind IN ('Http', 'Package'))
        AND (tc.Id IS NOT NULL OR tp.Id IS NOT NULL OR tg.Kind IN ('Http', 'Package'))
        """;
}
