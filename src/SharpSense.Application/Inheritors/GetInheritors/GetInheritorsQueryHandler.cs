using SharpSense.Application.Inheritors.Abstractions;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Inheritors.GetInheritors;

internal sealed class GetInheritorsQueryHandler(IInheritorFinder inheritorFinder)
    : IQueryHandler<GetInheritorsQuery, CodeNodeResult[]>
{
    public Task<CodeNodeResult[]> Handle(
        GetInheritorsQuery query,
        CancellationToken ct)
        => inheritorFinder.GetInheritors(query, ct);
}
