using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Trace.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GetImpactEndpoint
{
    public static IEndpointRouteBuilder MapGetImpactEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/impact", GetImpact)
            .WithName(nameof(GetImpact))
            .WithTags("Impact")
            .Produces<ImpactAnalysisResult>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        return builder;
    }

    private static async Task<IResult> GetImpact(
        ImpactRequest request,
        IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult> handler,
        ITraceNavigator navigator,
        CancellationToken ct)
    {
        if (request.NodeId <= 0 || request.MaxDepth is < 1 or > 10)
        {
            return UiProblemResults.Invalid("A positive nodeId and maxDepth between 1 and 10 are required.");
        }

        var identifier = request.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (await navigator.GetRootNode(identifier, ct) is null)
        {
            return UiProblemResults.MissingNode(request.NodeId);
        }

        return Results.Ok(await handler.Handle(new ImpactAnalysisQuery(identifier, request.MaxDepth), ct));
    }
}
