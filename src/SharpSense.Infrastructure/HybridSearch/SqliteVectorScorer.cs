using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.HybridSearch;

public sealed class SqliteVectorScorer : IVectorScorer
{
    public async Task<Dictionary<int, float>> GetScoresAsync(SharpSenseDbContext context,
        HybridSearchQuery query,
        IReadOnlyList<int> candidateIds,
        float[] queryVector,
        CancellationToken ct)
    {
        if (candidateIds.Count == 0)
        {
            return [];
        }

        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await context.Database.OpenConnectionAsync(ct);
        }

        var vectorScores = await GetCodeNodeScores(connection, candidateIds, queryVector, ct);
        if (!query.IncludeMemories)
        {
            return vectorScores;
        }

        var memoryScores = await GetMemoryScores(
            connection,
            candidateIds,
            queryVector,
            query.TagFilters,
            ct);

        foreach (var (candidateId, score) in memoryScores)
        {
            vectorScores[candidateId] = Math.Max(vectorScores.GetValueOrDefault(candidateId, 0f), score);
        }

        return vectorScores;
    }

    private static async Task<Dictionary<int, float>> GetCodeNodeScores(
        DbConnection connection,
        IReadOnlyList<int> candidateIds,
        float[] queryVector,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var parameterNames = new string[candidateIds.Count];
        for (var index = 0; index < candidateIds.Count; index++)
        {
            var parameterName = $"$id{index}";
            parameterNames[index] = parameterName;

            var idParameter = command.CreateParameter();
            idParameter.ParameterName = parameterName;
            idParameter.Value = candidateIds[index];
            command.Parameters.Add(idParameter);
        }

        var queryVectorParameter = command.CreateParameter();
        queryVectorParameter.ParameterName = "$queryVector";
        queryVectorParameter.Value = ConvertToBytes(queryVector);
        command.Parameters.Add(queryVectorParameter);

        command.CommandText = $$"""
            SELECT Id, vec_distance_cosine(VectorEmbedding, vec_f32($queryVector))
            FROM CodeNodes
            WHERE VectorEmbedding IS NOT NULL
              AND Id IN ({{string.Join(", ", parameterNames)}});
            """;

        var vectorScores = new Dictionary<int, float>(candidateIds.Count);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            var codeNodeId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            var distance = Convert.ToSingle(reader.GetValue(1), CultureInfo.InvariantCulture);
            vectorScores[codeNodeId] = Math.Clamp(1f - distance, 0f, 1f);
        }

        return vectorScores;
    }

    private static async Task<Dictionary<int, float>> GetMemoryScores(
        DbConnection connection,
        IReadOnlyList<int> candidateIds,
        float[] queryVector,
        string[]? tagFilters,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        var parameterNames = new string[candidateIds.Count];
        for (var index = 0; index < candidateIds.Count; index++)
        {
            var parameterName = $"$memoryId{index}";
            parameterNames[index] = parameterName;

            var idParameter = command.CreateParameter();
            idParameter.ParameterName = parameterName;
            idParameter.Value = candidateIds[index];
            command.Parameters.Add(idParameter);
        }

        var queryVectorParameter = command.CreateParameter();
        queryVectorParameter.ParameterName = "$queryVector";
        queryVectorParameter.Value = ConvertToBytes(queryVector);
        command.Parameters.Add(queryVectorParameter);

        var tagFilterClause = MemorySearchSql.BuildTagFilterClause(command, tagFilters, "MemoryNodes.TagsJson");
        command.CommandText = $$"""
            SELECT CodeNodes.Id,
                   MIN(vec_distance_cosine(MemoryNodes.VectorEmbedding, vec_f32($queryVector)))
            FROM MemoryNodes
            INNER JOIN CodeNodes ON CodeNodes.FullyQualifiedName = MemoryNodes.TargetFullyQualifiedName
            WHERE MemoryNodes.VectorEmbedding IS NOT NULL
              AND CodeNodes.Id IN ({{string.Join(", ", parameterNames)}}){{tagFilterClause}}
            GROUP BY CodeNodes.Id;
            """;

        var vectorScores = new Dictionary<int, float>(candidateIds.Count);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
            {
                continue;
            }

            var codeNodeId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            var distance = Convert.ToSingle(reader.GetValue(1), CultureInfo.InvariantCulture);
            vectorScores[codeNodeId] = Math.Clamp(1f - distance, 0f, 1f);
        }

        return vectorScores;
    }

    private static byte[] ConvertToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
