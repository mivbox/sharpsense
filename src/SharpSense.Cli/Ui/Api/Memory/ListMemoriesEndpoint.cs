using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Cli.Ui.Api;

internal static class ListMemoriesEndpoint
{
    public static IEndpointRouteBuilder MapListMemoriesEndpoint(this IEndpointRouteBuilder builder)
    {
        builder.MapGet("/list", ListMemories)
            .WithName(nameof(ListMemories))
            .Produces<ListMemoriesResult>()
            .ProducesProblem(400);

        return builder;
    }

    private static async Task<IResult> ListMemories(
        string? intents,
        string? tags,
        bool? staleOnly,
        string? searchText,
        int? limit,
        int? offset,
        IQueryHandler<ListMemoriesQuery, Result<ListMemoriesResult>> handler,
        CancellationToken ct)
    {
        var intentFilter = new List<MemoryIntent>();
        foreach (var raw in (intents ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<MemoryIntent>(raw, ignoreCase: true, out var intent) || !Enum.IsDefined(intent))
            {
                return UiProblemResults.Invalid($"Unknown memory intent '{raw}'.");
            }

            intentFilter.Add(intent);
        }

        var tagFilter = (tags ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var result = await handler.Handle(
            new ListMemoriesQuery(
                intentFilter.Count == 0 ? null : intentFilter.ToArray(),
                tagFilter.Length == 0 ? null : tagFilter,
                staleOnly ?? false,
                searchText,
                limit ?? 50,
                offset ?? 0),
            ct);

        return result.IsSuccess ? Results.Ok(result.Value) : UiProblemResults.Failure(result.Errors);
    }
}
