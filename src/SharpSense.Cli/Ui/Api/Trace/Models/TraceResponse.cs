using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Cli.Ui.Api;

public sealed record TraceResponse(
    CodeNodeResult Root,
    string Direction,
    CodeNodeResult[] Nodes,
    ImpactedDependencyEdge[] Dependencies,
    bool Truncated = false);
