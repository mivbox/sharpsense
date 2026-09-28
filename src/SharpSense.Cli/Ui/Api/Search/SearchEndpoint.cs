using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Cli.Ui.Api;

public sealed record SearchRequest(string Query, int Limit = 10);

internal static class SearchEndpoint
{
    public static void Map(WebApplication app) => app.MapPost(
        "/api/tools/search",
        async (SearchRequest request, IQueryHandler<HybridSearchQuery, HybridSearchResult> handler, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(request.Query) || request.Limit is < 1 or > 50)
        {
            return UiApiExtensions.Invalid("A query and limit between 1 and 50 are required.");
        }

        return Results.Ok(await handler.Handle(new HybridSearchQuery(request.Query, request.Limit), ct));
    })
        .WithName("SearchWorkspace")
        .WithTags("Search")
        .Produces<HybridSearchResult>()
        .ProducesProblem(400);
}
