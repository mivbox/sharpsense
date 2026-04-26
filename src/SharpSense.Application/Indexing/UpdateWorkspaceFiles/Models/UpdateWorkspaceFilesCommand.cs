using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;

public sealed record UpdateWorkspaceFilesCommand(
    IReadOnlyList<WorkspaceFileChange>? ChangedFiles = null,
    IProgress<IndexingProgress>? Progress = null);
