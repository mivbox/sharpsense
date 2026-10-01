using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class GetInheritorsEndpoint
{
    public static IEndpointRouteBuilder MapGetInheritorsEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/inheritors", GetInheritors)
            .WithName(nameof(GetInheritors))
            .WithTags("Inheritors")
            .Produces<CodeNodeResult[]>()
            .ProducesProblem(400)
            .ProducesProblem(404);

        return builder;
    }

    private static async Task<IResult> GetInheritors(
        InheritorsRequest request,
        IQueryHandler<GetInheritorsQuery, CodeNodeResult[]> handler,
        ITraceNavigator navigator,
        CancellationToken ct)
    {
        if (request.NodeId <= 0)
        {
            return UiProblemResults.Invalid("nodeId must be positive.");
        }

        if (await navigator.GetRootNode(
            request.NodeId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ct) is null)
        {
            return UiProblemResults.MissingNode(request.NodeId);
        }

        return Results.Ok(await handler.Handle(new GetInheritorsQuery(request.NodeId), ct));
    }
}
