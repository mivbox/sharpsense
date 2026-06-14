using Microsoft.EntityFrameworkCore;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence;

public sealed class SharpSenseDbContext(DbContextOptions<SharpSenseDbContext> options)
    : DbContext(options)
{
    public DbSet<DirectoryRecord> Directories => Set<DirectoryRecord>();

    public DbSet<DirectoryClosureRecord> DirectoryClosures => Set<DirectoryClosureRecord>();

    public DbSet<DocumentRecord> Documents => Set<DocumentRecord>();

    public DbSet<GraphNodeRecord> GraphNodes => Set<GraphNodeRecord>();

    public DbSet<ProjectNodeRecord> ProjectNodes => Set<ProjectNodeRecord>();

    public DbSet<CodeNodeRecord> CodeNodes => Set<CodeNodeRecord>();

    public DbSet<MemoryNodeRecord> MemoryNodes => Set<MemoryNodeRecord>();

    public DbSet<DependencyEdgeRecord> DependencyEdges => Set<DependencyEdgeRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(InfrastructureAssemblyMarker).Assembly);

}
