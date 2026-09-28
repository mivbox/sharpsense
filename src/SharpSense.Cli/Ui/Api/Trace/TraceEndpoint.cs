using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.GetTraceGraph.Models;
using SharpSense.Application.Trace.Models;

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

    private static async Task<IResult> Execute(
        TraceRequest request,
        IQueryHandler<GetTraceGraphQuery, Result<TraceGraphResult>> handler,
        CancellationToken ct)
    {
        TraceDirection? direction = request.Direction?.ToLowerInvariant() switch
        {
            "caller" => TraceDirection.Caller,
            "callee" => TraceDirection.Callee,
            _ => null
        };
        if (direction is null)
        {
            return UiApiExtensions.Invalid("A positive nodeId, caller/callee direction, and maxDepth between 1 and 10 are required.");
        }

        var result = await handler.Handle(new GetTraceGraphQuery(request.NodeId, direction.Value, request.MaxDepth), ct);
        if (result.IsFailed)
        {
            return UiApiExtensions.Failure(result.Errors);
        }

        var graph = result.Value;

        return Results.Ok(new TraceResponse(
            graph.Root,
            graph.Direction == TraceDirection.Caller ? "caller" : "callee",
            graph.Nodes,
            graph.Dependencies,
            graph.Truncated));
    }
}
