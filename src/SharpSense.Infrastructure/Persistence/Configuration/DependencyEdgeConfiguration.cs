using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class DependencyEdgeConfiguration : IEntityTypeConfiguration<DependencyEdgeRecord>
{
    public void Configure(EntityTypeBuilder<DependencyEdgeRecord> builder)
    {
        builder.ToTable("DependencyEdges");

        builder.HasKey(dependencyEdge => new
        {
            dependencyEdge.CallerNodeId,
            dependencyEdge.CalleeNodeId,
            dependencyEdge.EdgeType
        });

        builder.Property(dependencyEdge => dependencyEdge.CallerNodeId)
            .IsRequired();

        builder.Property(dependencyEdge => dependencyEdge.CalleeNodeId)
            .IsRequired();

        builder.Property(dependencyEdge => dependencyEdge.EdgeType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.HasOne<GraphNodeRecord>()
            .WithMany()
            .HasForeignKey(dependencyEdge => dependencyEdge.CallerNodeId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GraphNodeRecord>()
            .WithMany()
            .HasForeignKey(dependencyEdge => dependencyEdge.CalleeNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(dependencyEdge => dependencyEdge.CallerNodeId);
        builder.HasIndex(dependencyEdge => dependencyEdge.CalleeNodeId);
    }
}
