using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Memory.GetMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Cli.Ui.Api;

internal static class GetMemoriesEndpoint
{
    public static IEndpointRouteBuilder MapGetMemoriesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("", GetMemories)
            .WithName(nameof(GetMemories))
            .Produces<MemoryNode[]>()
            .ProducesProblem(400);

        return builder;
    }

    private static async Task<IResult> GetMemories(
        Guid[]? ids,
        IQueryHandler<GetMemoriesQuery, Result<IReadOnlyDictionary<Guid, MemoryNode>>> handler,
        CancellationToken ct)
    {
        if (ids is not { Length: > 0 })
        {
            return Results.Ok(Array.Empty<MemoryNode>());
        }

        if (ids.Length > 200)
        {
            return UiProblemResults.Invalid("At most 200 memory ids may be requested.");
        }

        var result = await handler.Handle(new GetMemoriesQuery(ids), ct);

        return result.IsSuccess ? Results.Ok(result.Value.Values.ToArray()) : UiProblemResults.Failure(result.Errors);
    }
}
