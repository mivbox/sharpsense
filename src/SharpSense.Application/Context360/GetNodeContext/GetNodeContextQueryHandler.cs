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

        var normalizedMaxRelated = Math.Clamp(query.MaxRelated, 1, 50);
        var context = await contextRepository.GetNodeContext(query.NodeId, normalizedMaxRelated, ct);

        return context is null
            ? Result.Fail(new ServiceError(ServiceErrorCode.NotFound, $"No persisted node exists for id {query.NodeId}."))
            : Result.Ok(context);
    }
}
