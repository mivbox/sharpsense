using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

internal static class SearchWorkspaceEndpoint
{
    public static IEndpointRouteBuilder MapSearchWorkspaceEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapPost("/search", SearchWorkspace)
            .WithName(nameof(SearchWorkspace))
            .WithTags("Search")
            .Produces<HybridSearchResult>()
            .ProducesProblem(400);

        return builder;
    }

    private static async Task<IResult> SearchWorkspace(
        SearchRequest request,
        IQueryHandler<HybridSearchQuery, Result<HybridSearchResult>> handler,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query) || request.Limit is < 1 or > 50)
        {
            return UiProblemResults.Invalid("A query and limit between 1 and 50 are required.");
        }

        var result = await handler.Handle(new HybridSearchQuery(request.Query, request.Limit), ct);

        return result.IsSuccess ? Results.Ok(result.Value) : UiProblemResults.Failure(result.Errors);
    }
}
