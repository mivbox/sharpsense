using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class ProjectNodeConfiguration : IEntityTypeConfiguration<ProjectNode>
{
    public void Configure(EntityTypeBuilder<ProjectNode> builder)
    {
        builder.ToTable("ProjectNodes");

        builder.HasKey(projectNode => projectNode.Id);

        builder.Property(projectNode => projectNode.Id)
            .ValueGeneratedNever()
            .HasMaxLength(2048);

        builder.Property(projectNode => projectNode.Name)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(projectNode => projectNode.RelativeFilePath)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(projectNode => projectNode.ContentHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(projectNode => projectNode.RelativeFilePath)
            .IsUnique();
    }
}
