using FluentResults;
using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;

namespace SharpSense.Application.Context360.GetNodeContext;

internal sealed class GetNodeContextQueryHandler(IContextRepository contextRepository)
    : IQueryHandler<GetNodeContextQuery, Result<Context360Result>>
{
    public async Task<Result<Context360Result>> Handle(GetNodeContextQuery query, CancellationToken ct)
    {
        if (query.NodeId <= 0)
        {
            return Result.Fail(new ServiceError(ServiceErrorCode.InvalidArgument, "NodeId must be greater than zero."));
        }

        if (query.IncludedSections?.Any(static section => !Enum.IsDefined(section)) == true)
        {
            return Result.Fail(new ServiceError(
                ServiceErrorCode.InvalidArgument,
                "Context sections must be values of: Callers, Implementers, Callees, Inherits, Parents, Children."));
        }

        var normalizedMaxRelated = Math.Clamp(query.MaxRelated, 1, 50);
        var normalizedSections = query.IncludedSections is { Length: > 0 }
            ? query.IncludedSections
                .Distinct()
                .OrderBy(static section => section)
                .ToArray()
            : null;
        var context = await contextRepository.GetNodeContext(
            query.NodeId,
            normalizedMaxRelated,
            normalizedSections,
            ct);

        return context is null
            ? Result.Fail(new ServiceError(
                ServiceErrorCode.NotFound,
                $"No persisted node exists for id {query.NodeId}."))
            : Result.Ok(context);
    }
}
