using SharpSense.Application.Indexing;

namespace SharpSense.Infrastructure.Storage;

/// <summary>
/// A named, home-owned selection of sources belonging to one repository checkout.
/// Source paths are relative to <see cref="RepositoryRoot"/>, never to the configuration file.
/// </summary>
public sealed class WorkspaceDefinition
{
    public int Version { get; set; } = 1;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string RepositoryRoot { get; set; } = string.Empty;

    public WorkspaceSource[] Sources { get; set; } = [];

}
