namespace SharpSense.Infrastructure.Storage;

/// <summary>
/// A resolved workspace definition and its storage locations, bound for one command or host.
/// </summary>
public sealed record WorkspaceSelection(
    WorkspaceDefinition Definition,
    string DirectoryPath,
    string ConfigurationPath,
    IRepositoryWorkspace Workspace);
