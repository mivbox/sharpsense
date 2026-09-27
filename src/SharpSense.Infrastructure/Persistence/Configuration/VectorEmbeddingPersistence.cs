using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace SharpSense.Infrastructure.Persistence.Configuration;

internal static class VectorEmbeddingPersistence
{
    internal static ValueConverter<float[]?, byte[]?> Converter
    {
        get;
    } = new(
        vectorEmbedding => ConvertToBytes(vectorEmbedding),
        bytes => ConvertToVectorEmbedding(bytes));

    internal static ValueComparer<float[]?> Comparer
    {
        get;
    } = new(
        (left, right) =>
            left == null && right == null ||
            left != null && right != null && left.SequenceEqual(right),
        vectorEmbedding => GetHashCode(vectorEmbedding),
        vectorEmbedding => vectorEmbedding == null ? null : vectorEmbedding.ToArray());

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
