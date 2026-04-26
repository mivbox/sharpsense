using SharpSense.Application.Features.HybridSearch.Contracts;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.HybridSearch;

public static class HybridSearchMapper
{
    public static HybridSearchHit ToSearchHit(CodeNode codeNode)
    {
        ArgumentNullException.ThrowIfNull(codeNode);

        return new HybridSearchHit(
            codeNode.Id,
            codeNode.CanonicalId,
            codeNode.ProjectId,
            codeNode.FullyQualifiedName,
            codeNode.DisplayName,
            codeNode.NodeType,
            codeNode.RelativeFilePath,
            codeNode.StartLine,
            codeNode.EndLine,
            codeNode.Summary);
    }

    public static HybridSearchHit[] ToSearchHit(IEnumerable<CodeNode> codeNodes)
        => [.. codeNodes.Select(ToSearchHit)];
}
