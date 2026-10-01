using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GetNodeContextEndpoint
{
    public static IEndpointRouteBuilder MapGetNodeContextEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/context", GetNodeContext)
            .WithName(nameof(GetNodeContext))
            .WithTags("Context")
            .Produces<Context360Result>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        return builder;
    }

    private static async Task<IResult> GetNodeContext(
        ContextRequest request,
        IQueryHandler<GetNodeContextQuery, Result<Context360Result>> handler,
        CancellationToken ct)
    {
        if (request.NodeId <= 0 || request.MaxRelated is < 1 or > 50)
        {
            return UiProblemResults.Invalid("A positive nodeId and maxRelated between 1 and 50 are required.");
        }

        var result = await handler.Handle(new GetNodeContextQuery(request.NodeId, request.MaxRelated), ct);

        return result.IsSuccess ? Results.Ok(result.Value) : UiProblemResults.Failure(result.Errors);
    }
}
