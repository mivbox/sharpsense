using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class GraphNodeRecordConfiguration : IEntityTypeConfiguration<GraphNodeRecord>
{
    public void Configure(EntityTypeBuilder<GraphNodeRecord> builder)
    {
        builder.ToTable("GraphNodes");

        builder.HasKey(node => node.Id);

        builder.Property(node => node.Id)
            .ValueGeneratedOnAdd();

        builder.Property(node => node.CanonicalId)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(node => node.Kind)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasIndex(node => node.CanonicalId)
            .IsUnique();
        builder.HasIndex(node => node.Kind);
    }
}
