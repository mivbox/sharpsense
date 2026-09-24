using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class MemoryNodeConfiguration : IEntityTypeConfiguration<MemoryNodeRecord>
{
    public void Configure(EntityTypeBuilder<MemoryNodeRecord> builder)
    {
        builder.ToTable("MemoryNodes");

        builder.HasKey(memoryNode => memoryNode.Id);

        builder.Property(memoryNode => memoryNode.Id)
            .ValueGeneratedNever();

        builder.Property(memoryNode => memoryNode.TargetCodeNodeId)
            .IsRequired();

        builder.Property(memoryNode => memoryNode.TargetCodeHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(memoryNode => memoryNode.Content)
            .IsRequired();

        builder.Property(memoryNode => memoryNode.ContentHash)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(memoryNode => memoryNode.TagsJson)
            .IsRequired()
            .HasColumnType("TEXT");

        builder.Property(memoryNode => memoryNode.Intent)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(memoryNode => memoryNode.VectorEmbedding)
            .HasColumnType("BLOB")
            .HasConversion(VectorEmbeddingPersistence.Converter, VectorEmbeddingPersistence.Comparer);

        builder.Property(memoryNode => memoryNode.CreatedAt)
            .IsRequired();

        builder.HasOne<CodeNodeRecord>()
            .WithMany()
            .HasForeignKey(memoryNode => memoryNode.TargetCodeNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(memoryNode => memoryNode.TargetCodeNodeId);
        builder.HasIndex(memoryNode => memoryNode.ContentHash);
        builder.HasIndex(memoryNode => memoryNode.CreatedAt);
    }
}
