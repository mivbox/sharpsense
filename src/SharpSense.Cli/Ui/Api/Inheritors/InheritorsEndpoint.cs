using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;

namespace SharpSense.Cli.Ui.Api;

public sealed record InheritorsRequest(int NodeId);

internal static class InheritorsEndpoint
{
    public static void Map(WebApplication app) => app.MapPost("/api/tools/inheritors", async (InheritorsRequest request, IQueryHandler<GetInheritorsQuery, CodeNodeResult[]> handler, ITraceNavigator navigator, CancellationToken ct) =>
    {
        if (request.NodeId <= 0) return UiApiExtensions.Invalid("nodeId must be positive.");
        if (await navigator.GetRootNode(request.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture), ct) is null) return UiApiExtensions.MissingNode(request.NodeId);
        return Results.Ok(await handler.Handle(new GetInheritorsQuery(request.NodeId), ct));
    }).WithName("GetInheritors").WithTags("Inheritors").Produces<CodeNodeResult[]>().ProducesProblem(400).ProducesProblem(404);
}
