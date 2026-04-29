using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class DirectoryClosureRecordConfiguration : IEntityTypeConfiguration<DirectoryClosureRecord>
{
    public void Configure(EntityTypeBuilder<DirectoryClosureRecord> builder)
    {
        builder.ToTable("DirectoryClosures");

        builder.HasKey(record => new { record.AncestorDirectoryId, record.DescendantDirectoryId });

        builder.Property(record => record.AncestorDirectoryId)
            .IsRequired();

        builder.Property(record => record.DescendantDirectoryId)
            .IsRequired();

        builder.Property(record => record.Depth)
            .IsRequired();

        builder.HasOne<DirectoryRecord>()
            .WithMany()
            .HasForeignKey(record => record.AncestorDirectoryId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DirectoryRecord>()
            .WithMany()
            .HasForeignKey(record => record.DescendantDirectoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(record => new { record.DescendantDirectoryId, record.AncestorDirectoryId });
        builder.ToTable(tableBuilder => tableBuilder.HasCheckConstraint("CK_DirectoryClosures_Depth", "Depth >= 0"));
    }
}
