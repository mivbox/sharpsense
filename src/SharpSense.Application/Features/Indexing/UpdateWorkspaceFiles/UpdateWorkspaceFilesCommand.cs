using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Features.Indexing.UpdateWorkspaceFiles;

public sealed record UpdateWorkspaceFilesCommand(
    IReadOnlyList<WorkspaceFileChange>? ChangedFiles = null,
    IProgress<IndexingProgress>? Progress = null);
