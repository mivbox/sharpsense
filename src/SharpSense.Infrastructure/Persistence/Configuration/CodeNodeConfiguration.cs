using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class CodeNodeConfiguration : IEntityTypeConfiguration<CodeNodeRecord>
{
    private static readonly ValueConverter<float[]?, byte[]?> VectorEmbeddingConverter = new(
        vectorEmbedding => ConvertToBytes(vectorEmbedding),
        bytes => ConvertToVectorEmbedding(bytes));

    private static readonly ValueComparer<float[]?> VectorEmbeddingComparer = new(
        (left, right) =>
            left == null && right == null ||
            left != null && right != null && left.SequenceEqual(right),
        vectorEmbedding => GetHashCode(vectorEmbedding),
        vectorEmbedding => vectorEmbedding == null ? null : vectorEmbedding.ToArray());

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
            .HasConversion(VectorEmbeddingConverter, VectorEmbeddingComparer);

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

    private static byte[]? ConvertToBytes(float[]? vectorEmbedding)
    {
        if (vectorEmbedding is null)
        {
            return null;
        }

        var bytes = new byte[vectorEmbedding.Length * sizeof(float)];
        Buffer.BlockCopy(vectorEmbedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[]? ConvertToVectorEmbedding(byte[]? bytes)
    {
        if (bytes is null)
        {
            return null;
        }

        if (bytes.Length % sizeof(float) != 0)
        {
            throw new InvalidOperationException("VectorEmbedding values must contain a whole number of single-precision floats.");
        }

        var vectorEmbedding = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vectorEmbedding, 0, bytes.Length);
        return vectorEmbedding;
    }

    private static int GetHashCode(float[]? vectorEmbedding)
    {
        if (vectorEmbedding is null)
        {
            return 0;
        }

        var hash = new HashCode();

        foreach (var value in vectorEmbedding)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}
