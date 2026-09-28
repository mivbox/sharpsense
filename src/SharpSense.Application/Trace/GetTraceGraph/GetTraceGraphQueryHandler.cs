using FluentResults;
using SharpSense.Application.ImpactAnalysis.Abstractions;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.GetTraceGraph.Models;
using SharpSense.Application.Trace.Models;
using SharpSense.Application.Trace.Trace.Models;
using System.Globalization;

namespace SharpSense.Application.Trace.GetTraceGraph;

internal sealed class GetTraceGraphQueryHandler(ITraceNavigator navigator, IImpactAnalyzer impactAnalyzer)
    : IQueryHandler<GetTraceGraphQuery, Result<TraceGraphResult>>
{
    public async Task<Result<TraceGraphResult>> Handle(GetTraceGraphQuery query, CancellationToken ct)
    {
        if (query.NodeId <= 0 || query.MaxDepth is < 1 or > 10 || query.Direction is not (TraceDirection.Caller or TraceDirection.Callee))
        {
            return Result.Fail(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "A positive nodeId, caller/callee direction, and maxDepth between 1 and 10 are required."));
        }

        var identifier = query.NodeId.ToString(CultureInfo.InvariantCulture);
        var root = await navigator.GetRootNode(identifier, ct);
        if (root is null)
        {
            return Result.Fail(new ServiceError(ServiceErrorCode.NotFound, $"No indexed code node exists for id {query.NodeId}."));
        }

        if (query.Direction == TraceDirection.Caller)
        {
            var impact = await impactAnalyzer.Analyze(new ImpactAnalysisQuery(identifier, query.MaxDepth), ct);
            var callers = impact.ImpactedNodes
                .Select(node => new CodeNodeResult(
                    node.Id,
                    node.CanonicalId,
                    node.ProjectId,
                    node.FullyQualifiedName,
                    node.DisplayName,
                    node.NodeType,
                    node.RelativeFilePath,
                    node.StartLine,
                    node.EndLine,
                    node.Summary))
                .ToArray();

            return Result.Ok(new TraceGraphResult(root, query.Direction, callers, impact.Dependencies));
        }

        // The root counts towards the visible graph budget and must also participate in cycle detection.
        const int maximumNodes = 1000;
        var visited = new Dictionary<int, CodeNodeResult>
        {
            [root.Id] = root
        };
        var frontier = new List<int>
        {
            root.Id
        };
        var truncated = false;

        for (var depth = 0; depth < query.MaxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (var id in frontier)
            {
                var callees = await navigator.GetCallees(new TraceQuery(id.ToString(CultureInfo.InvariantCulture)), ct);
                foreach (var node in callees)
                {
                    if (visited.ContainsKey(node.Id))
                    {
                        continue;
                    }

                    if (visited.Count >= maximumNodes)
                    {
                        truncated = true;
                        continue;
                    }

                    visited.Add(node.Id, node);
                    next.Add(node.Id);
                }
            }

            frontier = next;
        }

        var dependencies = await navigator.GetDependencies(visited.Values, ct);
        var nodes = visited.Values
            .Where(node => node.Id != root.Id)
            .ToArray();

        return Result.Ok(new TraceGraphResult(root, query.Direction, nodes, dependencies, truncated));
    }
}
