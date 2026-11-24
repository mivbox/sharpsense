using FluentResults;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;

namespace SharpSense.Application.Memory.ListMemories;

internal sealed class ListMemoriesQueryHandler(IMemoryRepository memoryRepository)
    : IQueryHandler<ListMemoriesQuery, Result<ListMemoriesResult>>
{
    private const int MinimumLimit = 1;
    private const int MaximumLimit = 200;

    public async Task<Result<ListMemoriesResult>> Handle(ListMemoriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit < MinimumLimit)
        {
            return Result.Fail<ListMemoriesResult>(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "Limit must be greater than zero."));
        }

        if (query.Offset < 0)
        {
            return Result.Fail<ListMemoriesResult>(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "Offset must not be negative."));
        }

        if (query.Intents?.Any(static intent => !Enum.IsDefined(intent)) == true)
        {
            return Result.Fail<ListMemoriesResult>(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "Memory intents must be values of: Convention, Invariant, Todo, Warning, Decision."));
        }

        if (query.Tags?.Any(static tag => tag is null) == true)
        {
            return Result.Fail<ListMemoriesResult>(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "Memory tag filters must not contain null values."));
        }

        var normalizedTags = query.Tags is { Length: > 0 }
            ? query.Tags
                .Select(static tag => tag.Trim()
                    .ToLowerInvariant())
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : null;
        var normalizedQuery = query with
        {
            Intents = query.Intents is { Length: > 0 }
                ? query.Intents
                    .Distinct()
                    .ToArray()
                : null,
            Tags = normalizedTags is { Length: > 0 }
                ? normalizedTags
                : null,
            SearchText = string.IsNullOrWhiteSpace(query.SearchText)
                ? null
                : query.SearchText.Trim(),
            Limit = Math.Clamp(query.Limit, MinimumLimit, MaximumLimit)
        };

        var result = await memoryRepository.ListMemories(normalizedQuery, ct);

        return Result.Ok(result);
    }
}
