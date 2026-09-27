using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

internal sealed class CodeNodeConfiguration : IEntityTypeConfiguration<CodeNodeRecord>
{
    public void Configure(EntityTypeBuilder<CodeNodeRecord> builder)
    {
        builder.ToTable("CodeNodes");

        builder.HasKey(codeNode => codeNode.Id);

        builder.Property(codeNode => codeNode.Id)
            .ValueGeneratedNever();

        builder.Property(codeNode => codeNode.ProjectNodeId);

        builder.Property(codeNode => codeNode.DocumentId)
            .IsRequired();

        builder.Property(codeNode => codeNode.FullyQualifiedName)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(codeNode => codeNode.DisplayName)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(codeNode => codeNode.NodeType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(64);

        builder.Property(codeNode => codeNode.StartLine)
            .IsRequired();

        builder.Property(codeNode => codeNode.EndLine)
            .IsRequired();

        builder.Property(codeNode => codeNode.Summary)
            .IsRequired();

        builder.Property(codeNode => codeNode.SearchText)
            .IsRequired();

        builder.Property(codeNode => codeNode.BodyHash)
            .HasMaxLength(128);

        builder.Property(codeNode => codeNode.VectorEmbedding)
            .HasColumnType("BLOB")
            .HasConversion(VectorEmbeddingPersistence.Converter, VectorEmbeddingPersistence.Comparer);

        builder.HasOne<GraphNodeRecord>()
            .WithOne()
            .HasForeignKey<CodeNodeRecord>(codeNode => codeNode.Id)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProjectNodeRecord>()
            .WithMany()
            .HasForeignKey(codeNode => codeNode.ProjectNodeId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<DocumentRecord>()
            .WithMany()
            .HasForeignKey(codeNode => codeNode.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(codeNode => codeNode.ProjectNodeId);
        builder.HasIndex(codeNode => codeNode.DocumentId);
        builder.HasIndex(codeNode => codeNode.FullyQualifiedName);
    }
}
