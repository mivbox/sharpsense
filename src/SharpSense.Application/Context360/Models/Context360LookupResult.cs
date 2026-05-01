using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Context360.Models;

public sealed record Context360LookupResult(
    CodeNodeResult TargetNode,
    CodeNodeResult[] Callers,
    CodeNodeResult[] Implementers,
    CodeNodeResult[] Callees,
    CodeNodeResult[] Inherits);
