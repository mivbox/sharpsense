using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Cli.Ui.Api;

internal static class GetNodeMemoriesEndpoint
{
    public static IEndpointRouteBuilder MapGetNodeMemoriesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/node/{nodeId:int}", GetNodeMemories)
            .WithName(nameof(GetNodeMemories))
            .Produces<MemoryNode[]>()
            .ProducesProblem(400);

        return builder;
    }

    private static async Task<IResult> GetNodeMemories(
        int nodeId,
        string? intents,
        IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>> handler,
        CancellationToken ct)
    {
        if (nodeId <= 0)
        {
            return UiProblemResults.Invalid("nodeId must be positive.");
        }

        var filter = new List<MemoryIntent>();
        foreach (var raw in (intents ?? "").Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<MemoryIntent>(raw, true, out var intent) || !Enum.IsDefined(intent))
            {
                return UiProblemResults.Invalid($"Unknown memory intent '{raw}'.");
            }

            filter.Add(intent);
        }

        var result = await handler.Handle(
            new GetNodeMemoriesQuery(
                nodeId,
                filter.Count == 0 ? null : filter.ToArray()),
            ct);

        return result.IsSuccess ? Results.Ok(result.Value) : UiProblemResults.Failure(result.Errors);
    }
}
