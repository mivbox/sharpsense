using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Domain.KnowledgeGraph.Edges;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class DependencyEdgeConfiguration : IEntityTypeConfiguration<DependencyEdge>
{
    public void Configure(EntityTypeBuilder<DependencyEdge> builder)
    {
        builder.ToTable("DependencyEdges");

        builder.HasKey(dependencyEdge => new
        {
            dependencyEdge.CallerId,
            dependencyEdge.CalleeId,
            dependencyEdge.EdgeType
        });

        builder.Property(dependencyEdge => dependencyEdge.CallerId)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(dependencyEdge => dependencyEdge.CalleeId)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(dependencyEdge => dependencyEdge.EdgeType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.HasIndex(dependencyEdge => dependencyEdge.CallerId);
        builder.HasIndex(dependencyEdge => dependencyEdge.CalleeId);
    }
}
