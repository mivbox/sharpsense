using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.Trace.Models;
using System.Globalization;

namespace SharpSense.Cli.Ui.Api;

public sealed record TraceRequest(int NodeId, string Direction = "callee", int MaxDepth = 3);
public sealed record TraceResponse(CodeNodeResult Root, string Direction, CodeNodeResult[] Nodes, ImpactedDependencyEdge[] Dependencies, bool Truncated = false);

internal static class TraceEndpoint
{
    public static void Map(WebApplication app) => app.MapPost("/api/tools/trace", Execute)
        .WithName("TraceNode")
        .WithTags("Trace")
        .Produces<TraceResponse>()
        .ProducesProblem(400)
        .ProducesProblem(404);

    private static async Task<IResult> Execute(TraceRequest request,
        ITraceNavigator navigator,
        IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult> impactHandler,
        CancellationToken ct)
    {
        var direction = request.Direction?.ToLowerInvariant();
        if (request.NodeId <= 0 || request.MaxDepth is < 1 or > 10 || direction is not ("caller" or "callee"))
        {
            return UiApiExtensions.Invalid("A positive nodeId, caller/callee direction, and maxDepth between 1 and 10 are required.");
        }

        var identifier = request.NodeId.ToString(CultureInfo.InvariantCulture);
        var root = await navigator.GetRootNode(identifier, ct);
        if (root is null)
        {
            return UiApiExtensions.MissingNode(request.NodeId);
        }

        if (direction == "caller")
        {
            var result = await impactHandler.Handle(new ImpactAnalysisQuery(identifier, request.MaxDepth), ct);

            return Results.Ok(new TraceResponse(
                root,
                direction,
                result.ImpactedNodes.Select(node => new CodeNodeResult(
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
                    .ToArray(),
                result.Dependencies));
        }

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
        for (var depth = 0; depth < request.MaxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (var id in frontier)
            {
                foreach (var node in await navigator.GetCallees(new TraceQuery(id.ToString(CultureInfo.InvariantCulture)), ct))
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

        return Results.Ok(new TraceResponse(
            root,
            direction,
            visited.Values.Where(node => node.Id != root.Id)
                .ToArray(),
            dependencies,
            truncated));
    }
}
