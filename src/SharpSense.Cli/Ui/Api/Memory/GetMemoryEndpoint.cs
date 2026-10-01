using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Memory.GetMemory.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Cli.Ui.Api;

internal static class GetMemoryEndpoint
{
    public static IEndpointRouteBuilder MapGetMemoryEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{memoryId:guid}", GetMemory)
            .WithName(nameof(GetMemory))
            .Produces<MemoryNode>()
            .ProducesProblem(404);

        return builder;
    }

    private static async Task<IResult> GetMemory(
        Guid memoryId,
        IQueryHandler<GetMemoryQuery, Result<MemoryNode>> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new GetMemoryQuery(memoryId), ct);

        return result.IsSuccess ? Results.Ok(result.Value) : UiProblemResults.Failure(result.Errors, 404);
    }
}
