using SharpSense.Application.Indexing;

namespace SharpSense.Infrastructure.Storage;

// Keep the legacy YAML key at the persistence boundary. New files serialize WorkspaceDefinition directly.
internal sealed class WorkspaceYamlDefinition
{
    public int Version { get; set; } = 1;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? WorkspaceRoot { get; set; }

    public string? RepositoryRoot { get; set; }

    public WorkspaceSource[] Sources { get; set; } = [];
}
