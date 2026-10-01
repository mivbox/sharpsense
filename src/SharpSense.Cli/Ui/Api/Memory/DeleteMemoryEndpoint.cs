using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class DeleteMemoryEndpoint
{
    public static IEndpointRouteBuilder MapDeleteMemoryEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{memoryId:guid}", DeleteMemory)
            .WithName(nameof(DeleteMemory))
            .Produces<DeleteMemoryResponse>()
            .ProducesProblem(404);

        return builder;
    }

    private static async Task<IResult> DeleteMemory(
        Guid memoryId,
        ICommandHandler<DeleteMemoryCommand, Result> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new DeleteMemoryCommand(memoryId), ct);

        return result.IsSuccess
            ? Results.Ok(new DeleteMemoryResponse(true, memoryId))
            : UiProblemResults.Failure(result.Errors, 404);
    }
}
