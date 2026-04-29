using Microsoft.EntityFrameworkCore;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.Persistence;

public sealed class SharpSenseDbContext(DbContextOptions<SharpSenseDbContext> options)
    : DbContext(options)
{
    public DbSet<ProjectNode> ProjectNodes => Set<ProjectNode>();

    public DbSet<CodeNode> CodeNodes => Set<CodeNode>();

    public DbSet<WorkspaceTreeNode> WorkspaceTreeNodes => Set<WorkspaceTreeNode>();

    public DbSet<DependencyEdge> DependencyEdges => Set<DependencyEdge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(InfrastructureAssemblyMarker).Assembly);

}
