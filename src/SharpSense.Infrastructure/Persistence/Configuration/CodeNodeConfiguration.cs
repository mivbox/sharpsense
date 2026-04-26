using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SharpSense.Domain.KnowledgeGraph.Nodes;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class CodeNodeConfiguration : IEntityTypeConfiguration<CodeNode>
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

    public void Configure(EntityTypeBuilder<CodeNode> builder)
    {
        builder.ToTable("CodeNodes");

        builder.HasKey(codeNode => codeNode.Id);

        builder.Property(codeNode => codeNode.Id)
            .ValueGeneratedOnAdd();

        builder.Property(codeNode => codeNode.CanonicalId)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(codeNode => codeNode.ProjectId)
            .HasMaxLength(2048);

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

        builder.Property(codeNode => codeNode.RelativeFilePath)
            .IsRequired()
            .HasMaxLength(2048);

        builder.Property(codeNode => codeNode.StartLine)
            .IsRequired();

        builder.Property(codeNode => codeNode.EndLine)
            .IsRequired();

        builder.Property(codeNode => codeNode.Summary)
            .IsRequired();

        builder.Property(codeNode => codeNode.VectorEmbedding)
            .HasColumnType("BLOB")
            .HasConversion(VectorEmbeddingConverter, VectorEmbeddingComparer);

        builder.HasIndex(codeNode => codeNode.CanonicalId)
            .IsUnique();
        builder.HasIndex(codeNode => codeNode.ProjectId);
        builder.HasIndex(codeNode => codeNode.FullyQualifiedName);
        builder.HasIndex(codeNode => codeNode.RelativeFilePath);
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
