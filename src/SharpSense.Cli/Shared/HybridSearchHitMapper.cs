using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Cli.Shared;

internal static class HybridSearchHitMapper
{
    public static CodeNodeResult[] Map(IEnumerable<HybridSearchHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);

        return
        [
            .. hits
                .Select(static hit => new CodeNodeResult(
                    hit.Id,
                    hit.CanonicalId,
                    hit.ProjectId,
                    hit.FullyQualifiedName,
                    hit.DisplayName,
                    hit.NodeType,
                    hit.RelativeFilePath,
                    hit.StartLine,
                    hit.EndLine,
                    hit.Summary))
        ];
    }
}
