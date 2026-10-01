using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Trace.GetTraceGraph.Models;
using SharpSense.Application.Trace.Models;

namespace SharpSense.Cli.Ui.Api;

internal static class TraceNodeEndpoint
{
    public static IEndpointRouteBuilder MapTraceNodeEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/trace", TraceNode)
            .WithName(nameof(TraceNode))
            .WithTags("Trace")
            .Produces<TraceResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        return builder;
    }

    private static async Task<IResult> TraceNode(
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
            return UiProblemResults.Invalid("A positive nodeId, caller/callee direction, and maxDepth between 1 and 10 are required.");
        }

        var result = await handler.Handle(
            new GetTraceGraphQuery(
                request.NodeId,
                direction.Value,
                request.MaxDepth),
            ct);
        if (result.IsFailed)
        {
            return UiProblemResults.Failure(result.Errors);
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
