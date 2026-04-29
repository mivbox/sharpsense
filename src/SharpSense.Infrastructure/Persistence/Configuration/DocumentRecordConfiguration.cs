using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class DocumentRecordConfiguration : IEntityTypeConfiguration<DocumentRecord>
{
    public void Configure(EntityTypeBuilder<DocumentRecord> builder)
    {
        builder.ToTable("Documents");

        builder.HasKey(document => document.Id);

        builder.Property(document => document.Id)
            .ValueGeneratedOnAdd();

        builder.Property(document => document.DirectoryId)
            .IsRequired();

        builder.Property(document => document.FileName)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(document => document.Extension)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(document => document.RelativePath)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(document => document.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasOne<DirectoryRecord>()
            .WithMany()
            .HasForeignKey(document => document.DirectoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(document => new { document.DirectoryId, document.FileName })
            .IsUnique();
        builder.HasIndex(document => document.RelativePath)
            .IsUnique();
        builder.HasIndex(document => document.DirectoryId);
    }
}
