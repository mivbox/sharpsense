using SharpSense.Application.Context360.Abstractions;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Context360.GetNodeContext;

internal sealed class GetNodeContextQueryHandler(IContextRepository contextRepository)
    : IQueryHandler<GetNodeContextQuery, Context360Result>
{
    public async Task<Context360Result> Handle(GetNodeContextQuery query,
        CancellationToken ct)
    {
        if (query.NodeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query.NodeId), query.NodeId, "NodeId must be greater than zero.");
        }

        var normalizedMaxRelated = Math.Clamp(query.MaxRelated, 1, 50);
        var context = await contextRepository.GetNodeContext(
            query.NodeId,
            normalizedMaxRelated,
            ct);

        return context ?? throw new InvalidOperationException($"No persisted node exists for id {query.NodeId}.");
    }
}
