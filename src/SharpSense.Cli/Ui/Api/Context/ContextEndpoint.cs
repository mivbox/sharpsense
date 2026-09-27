using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Trace.Abstractions;

namespace SharpSense.Cli.Ui.Api;

public sealed record ContextRequest(int NodeId, int MaxRelated = 10);

internal static class ContextEndpoint
{
    public static void Map(WebApplication app) => app.MapPost(
        "/api/tools/context",
        async (ContextRequest request, IQueryHandler<GetNodeContextQuery, Context360Result> handler, ITraceNavigator navigator, CancellationToken ct) =>
    {
        if (request.NodeId <= 0 || request.MaxRelated is < 1 or > 50)
        {
            return UiApiExtensions.Invalid("A positive nodeId and maxRelated between 1 and 50 are required.");
        }

        if (await navigator.GetRootNode(request.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture), ct) is null)
        {
            return UiApiExtensions.MissingNode(request.NodeId);
        }

        return Results.Ok(await handler.Handle(new GetNodeContextQuery(request.NodeId, request.MaxRelated), ct));
    })
        .WithName("GetNodeContext")
        .WithTags("Context")
        .Produces<Context360Result>()
        .ProducesProblem(400)
        .ProducesProblem(404);
}
