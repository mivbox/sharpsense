using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Trace.Abstractions;

namespace SharpSense.Cli.Ui.Api;

public sealed record ImpactRequest(int NodeId, int MaxDepth = 3);

internal static class ImpactEndpoint
{
    public static void Map(WebApplication app) => app.MapPost("/api/tools/impact", async (ImpactRequest request, IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult> handler, ITraceNavigator navigator, CancellationToken ct) =>
    {
        if (request.NodeId <= 0 || request.MaxDepth is < 1 or > 10) return UiApiExtensions.Invalid("A positive nodeId and maxDepth between 1 and 10 are required.");
        var identifier = request.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (await navigator.GetRootNode(identifier, ct) is null) return UiApiExtensions.MissingNode(request.NodeId);
        return Results.Ok(await handler.Handle(new ImpactAnalysisQuery(identifier, request.MaxDepth), ct));
    }).WithName("GetImpact").WithTags("Impact").Produces<ImpactAnalysisResult>().ProducesProblem(400).ProducesProblem(404);
}
