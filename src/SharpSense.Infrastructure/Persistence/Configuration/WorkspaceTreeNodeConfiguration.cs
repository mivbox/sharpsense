using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class WorkspaceTreeNodeConfiguration : IEntityTypeConfiguration<WorkspaceTreeNode>
{
    public void Configure(EntityTypeBuilder<WorkspaceTreeNode> builder)
    {
        builder.ToTable("WorkspaceTreeNodes");

        builder.HasKey(node => node.Id);

        builder.Property(node => node.Id)
            .ValueGeneratedNever()
            .HasMaxLength(2048);

        builder.Property(node => node.ParentId)
            .HasMaxLength(2048);

        builder.Property(node => node.Path)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(node => node.Label)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(node => node.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(node => node.ProjectId)
            .HasMaxLength(2048);

        builder.Property(node => node.HasChildren)
            .IsRequired();

        builder.Property(node => node.IsSelectable)
            .IsRequired();

        builder.HasIndex(node => node.ParentId);
        builder.HasIndex(node => node.Path)
            .IsUnique();
        builder.HasIndex(node => node.ProjectId);
    }
}
