using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

public sealed record ContextRequest(int NodeId, int MaxRelated = 10);

internal static class ContextEndpoint
{
    public static void Map(WebApplication app) => app.MapPost("/api/tools/context", Execute)
        .WithName("GetNodeContext")
        .WithTags("Context")
        .Produces<Context360Result>()
        .ProducesProblem(400)
        .ProducesProblem(404);

    private static async Task<IResult> Execute(
        ContextRequest request,
        IQueryHandler<GetNodeContextQuery, Result<Context360Result>> handler,
        CancellationToken ct)
    {
        if (request.NodeId <= 0 || request.MaxRelated is < 1 or > 50)
        {
            return UiApiExtensions.Invalid("A positive nodeId and maxRelated between 1 and 50 are required.");
        }

        var result = await handler.Handle(new GetNodeContextQuery(request.NodeId, request.MaxRelated), ct);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : UiApiExtensions.Failure(result.Errors);
    }
}
