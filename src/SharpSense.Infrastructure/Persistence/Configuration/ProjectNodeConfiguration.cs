using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class ProjectNodeConfiguration : IEntityTypeConfiguration<ProjectNodeRecord>
{
    public void Configure(EntityTypeBuilder<ProjectNodeRecord> builder)
    {
        builder.ToTable("ProjectNodes");

        builder.HasKey(projectNode => projectNode.Id);

        builder.Property(projectNode => projectNode.Id)
            .ValueGeneratedNever();

        builder.Property(projectNode => projectNode.Name)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(projectNode => projectNode.ProjectDocumentId)
            .IsRequired()
            ;

        builder.Property(projectNode => projectNode.ContentHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasOne<GraphNodeRecord>()
            .WithOne()
            .HasForeignKey<ProjectNodeRecord>(projectNode => projectNode.Id)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DocumentRecord>()
            .WithMany()
            .HasForeignKey(projectNode => projectNode.ProjectDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(projectNode => projectNode.ProjectDocumentId)
            .IsUnique();
    }
}
