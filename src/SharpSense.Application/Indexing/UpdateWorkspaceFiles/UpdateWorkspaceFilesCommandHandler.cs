using FluentResults;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.Indexing.UpdateWorkspaceFiles;

internal sealed class UpdateWorkspaceFilesCommandHandler(
    ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>> workspaceIndexer,
    IWorkspaceChangeFilter workspaceChangeFilter)
    : ICommandHandler<UpdateWorkspaceFilesCommand, Result<UpdateWorkspaceFilesOutcome>>
{
    public async Task<Result<UpdateWorkspaceFilesOutcome>> Handle(
        UpdateWorkspaceFilesCommand command,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command.ChangedFiles);

        if (command.ChangedFiles.Count == 0 || workspaceChangeFilter.IsRelevant(command.ChangedFiles) == false)
        {
            if (command.Notifier is not null)
            {
                new AnalysisOperation(command.Notifier, AnalysisOperationKind.Incremental).Ignored();
            }

            return Result.Ok(new UpdateWorkspaceFilesOutcome(0, 0, 0, IndexCommitted: false));
        }

        // Reconcile the complete selected graph. A change in one contribution must not
        // replace another source or leave reverse links stale. C# source-only edits can
        // still reuse their loaded Roslyn workspaces through ChangedFiles.
        var result = await workspaceIndexer.Handle(
            new IndexWorkspaceCommand(
                Progress: command.Progress,
                EmbeddingProgress: command.EmbeddingProgress,
                ChangedFiles: command.ChangedFiles,
                Notifier: command.Notifier),
            ct);

        if (result.IsFailed)
        {
            return Result.Fail(result.Errors);
        }

        return Result.Ok(new UpdateWorkspaceFilesOutcome(
            result.Value.ProjectsIndexed,
            result.Value.CodeNodesPersisted,
            result.Value.DependencyEdgesPersisted));
    }
}
