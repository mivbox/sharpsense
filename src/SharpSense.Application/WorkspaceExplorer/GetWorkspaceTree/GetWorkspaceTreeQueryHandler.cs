using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.WorkspaceExplorer.Abstractions;
using SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree.Models;
using SharpSense.Application.WorkspaceExplorer.Models;
using System.Diagnostics.CodeAnalysis;

namespace SharpSense.Application.WorkspaceExplorer.GetWorkspaceTree;

[ExcludeFromCodeCoverage(Justification = "Application layer proxy, no logic to test.")]
internal sealed class GetWorkspaceTreeQueryHandler(IWorkspaceTreeRepository workspaceTreeRepository)
    : IQueryHandler<GetWorkspaceTreeQuery, WorkspaceTreeResult>
{
    public Task<WorkspaceTreeResult> Handle(
        GetWorkspaceTreeQuery query,
        CancellationToken ct)
        => workspaceTreeRepository.GetTree(query.Path, ct);
}
