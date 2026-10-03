using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Memory.AttachMemory.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Cli.Ui.Api;

internal static class AddMemoryEndpoint
{
    public static IEndpointRouteBuilder MapAddMemoryEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/node/{nodeId:int}", AddMemory)
            .WithName(nameof(AddMemory))
            .Produces<AddMemoryResponse>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        return builder;
    }

    private static async Task<IResult> AddMemory(
        int nodeId,
        AddMemoryRequest request,
        ICommandHandler<AttachMemoryCommand, Result<MemoryNode>> handler,
        CancellationToken ct)
    {
        if (nodeId <= 0 || string.IsNullOrWhiteSpace(request.Content) || !Enum.IsDefined(request.Intent))
        {
            return UiProblemResults.Invalid("A positive nodeId, non-empty content, and valid intent are required.");
        }

        var result = await handler.Handle(
            new AttachMemoryCommand(
                nodeId,
                request.Content,
                request.Tags ?? [],
                request.Intent),
            ct);

        return result.IsSuccess
            ? Results.Ok(new AddMemoryResponse(true, nodeId, request.Intent))
            : UiProblemResults.Failure(result.Errors);
    }
}
