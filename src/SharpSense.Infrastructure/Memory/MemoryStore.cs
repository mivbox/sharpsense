using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Memory;

public sealed class MemoryStore(
    IDbContextFactory<SharpSenseDbContext> dbContextFactory,
    IEmbeddingGenerator embeddingGenerator)
    : IMemoryReader, IMemoryWriter
{
    public async Task<Result> AttachMemory(
        int nodeId,
        string content,
        string[]? tags,
        CancellationToken ct)
    {
        if (nodeId <= 0)
        {
            return Result.Fail("Node id must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Fail("Content must not be empty.");
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var targetNode = await context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => codeNode.Id == nodeId)
            .Select(static codeNode => new
            {
                codeNode.FullyQualifiedName,
                codeNode.BodyHash
            })
            .FirstOrDefaultAsync(ct);
        if (targetNode is null)
        {
            return Result.Fail($"No persisted node exists for id {nodeId}.");
        }

        var normalizedContent = content.Trim();
        var normalizedTags = NormalizeTags(tags);
        var contentHash = ComputeHash(normalizedContent);
        var vectorEmbedding = await context.MemoryNodes
                .AsNoTracking()
                .Where(memoryNode => memoryNode.ContentHash == contentHash)
                .Select(memoryNode => memoryNode.VectorEmbedding)
                .FirstOrDefaultAsync(ct)
            ?? (await embeddingGenerator.Generate(normalizedContent, ct)).Vector;

        context.MemoryNodes.Add(
            new MemoryNodeRecord
            {
                Id = Guid.NewGuid(),
                TargetFullyQualifiedName = targetNode.FullyQualifiedName,
                TargetCodeHash = targetNode.BodyHash ?? string.Empty,
                Content = normalizedContent,
                ContentHash = contentHash,
                TagsJson = JsonSerializer.Serialize(normalizedTags),
                VectorEmbedding = vectorEmbedding,
                CreatedAt = DateTimeOffset.UtcNow
            });

        await context.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyDictionary<int, MemoryNode[]>> GetNodeMemories(
        IReadOnlyCollection<int> nodeIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        var normalizedNodeIds = nodeIds
            .Where(static nodeId => nodeId > 0)
            .Distinct()
            .Order()
            .ToArray();
        if (normalizedNodeIds.Length == 0)
        {
            return new Dictionary<int, MemoryNode[]>();
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var codeNodes = await context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => normalizedNodeIds.Contains(codeNode.Id))
            .Select(static codeNode => new
            {
                codeNode.Id,
                codeNode.FullyQualifiedName,
                codeNode.BodyHash
            })
            .ToArrayAsync(ct);
        if (codeNodes.Length == 0)
        {
            return new Dictionary<int, MemoryNode[]>();
        }

        var codeHashesByFullyQualifiedName = codeNodes.ToDictionary(
            static codeNode => codeNode.FullyQualifiedName,
            static codeNode => codeNode.BodyHash ?? string.Empty,
            StringComparer.Ordinal);
        var memories = await context.MemoryNodes
            .AsNoTracking()
            .Where(memoryNode => codeHashesByFullyQualifiedName.Keys.Contains(memoryNode.TargetFullyQualifiedName))
            .ToArrayAsync(ct);
        var memoriesByFullyQualifiedName = memories
            .OrderBy(memoryNode => memoryNode.CreatedAt)
            .GroupBy(static memoryNode => memoryNode.TargetFullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                group => group.ToArray(),
                StringComparer.Ordinal);

        return codeNodes.ToDictionary(
            static codeNode => codeNode.Id,
            codeNode => memoriesByFullyQualifiedName.TryGetValue(codeNode.FullyQualifiedName, out var records)
                ? records.Select(record => Map(record, codeHashesByFullyQualifiedName[codeNode.FullyQualifiedName])).ToArray()
                : []);
    }

    internal static string[] NormalizeTags(string[]? tags)
        => tags is null
            ? []
            : [..
                tags
                    .Select(static tag => tag.Trim().ToLowerInvariant())
                    .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static tag => tag, StringComparer.Ordinal)];

    private static MemoryNode Map(MemoryNodeRecord record, string currentCodeHash)
        => new(
            record.Id,
            record.TargetFullyQualifiedName,
            record.TargetCodeHash,
            record.Content,
            record.ContentHash,
            DeserializeTags(record.TagsJson),
            record.CreatedAt,
            !string.Equals(currentCodeHash, record.TargetCodeHash, StringComparison.Ordinal));

    private static string[] DeserializeTags(string tagsJson)
        => string.IsNullOrWhiteSpace(tagsJson)
            ? []
            : JsonSerializer.Deserialize<string[]>(tagsJson) ?? [];

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
