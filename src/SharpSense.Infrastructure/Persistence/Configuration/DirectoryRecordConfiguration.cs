using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

internal sealed class DirectoryRecordConfiguration : IEntityTypeConfiguration<DirectoryRecord>
{
    public void Configure(EntityTypeBuilder<DirectoryRecord> builder)
    {
        builder.ToTable("Directories");

        builder.HasKey(directory => directory.Id);

        builder.Property(directory => directory.Id)
            .ValueGeneratedOnAdd();

        builder.Property(directory => directory.ParentId);

        builder.Property(directory => directory.Path)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(directory => directory.Name)
            .IsRequired()
            .HasMaxLength(512);

        builder.HasOne<DirectoryRecord>()
            .WithMany()
            .HasForeignKey(directory => directory.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(directory => directory.Path)
            .IsUnique();
        builder.HasIndex(directory => new
        {
            directory.ParentId,
            directory.Name
        })
            .IsUnique();
        builder.HasIndex(directory => directory.ParentId);
    }
}
