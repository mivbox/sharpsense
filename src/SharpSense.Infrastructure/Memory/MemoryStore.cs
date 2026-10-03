using FluentResults;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpSense.Infrastructure.Memory;

internal sealed class MemoryStore(
    IDbContextFactory<SharpSenseDbContext> dbContextFactory,
    IEmbeddingGenerator embeddingGenerator)
    : IMemoryRepository
{
    public async Task<Result<MemoryNode>> AttachMemory(
        int nodeId,
        string content,
        string[]? tags,
        MemoryIntent intent,
        CancellationToken ct)
    {
        if (nodeId <= 0)
        {
            return Result.Fail<MemoryNode>("Node id must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Fail<MemoryNode>("Content must not be empty.");
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var targetNode = await context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => codeNode.Id == nodeId)
            .Select(static codeNode => new
            {
                codeNode.BodyHash,
                codeNode.FullyQualifiedName
            })
            .FirstOrDefaultAsync(ct);
        if (targetNode is null)
        {
            return Result.Fail<MemoryNode>($"No persisted node exists for id {nodeId}.");
        }

        var normalizedContent = content.Trim();
        var normalizedTags = MemoryTags.Normalize(tags);
        var contentHash = ComputeHash(normalizedContent);
        var vectorEmbedding = await context.MemoryNodes
            .AsNoTracking()
            .Where(memoryNode => memoryNode.ContentHash == contentHash)
            .Select(memoryNode => memoryNode.VectorEmbedding)
            .FirstOrDefaultAsync(ct)
            ?? (await embeddingGenerator.Generate(normalizedContent, ct)).Vector;

        var memory = new MemoryNodeRecord
        {
            Id = Guid.NewGuid(),
            TargetCodeNodeId = nodeId,
            TargetCodeHash = targetNode.BodyHash ?? string.Empty,
            Content = normalizedContent,
            ContentHash = contentHash,
            TagsJson = JsonSerializer.Serialize(normalizedTags),
            Intent = NormalizeIntent(intent)
                    .ToString(),
            VectorEmbedding = vectorEmbedding,
            CreatedAt = DateTimeOffset.UtcNow
        };
        context.MemoryNodes.Add(memory);

        await context.SaveChangesAsync(ct);

        return Result.Ok(Map(memory, targetNode.FullyQualifiedName, targetNode.BodyHash ?? string.Empty));
    }

    public async Task<Result> DeleteMemory(Guid memoryId, CancellationToken ct)
    {
        if (memoryId == Guid.Empty)
        {
            return Result.Fail("Memory id must not be empty.");
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var memory = await context.MemoryNodes.FirstOrDefaultAsync(record => record.Id == memoryId, ct);
        if (memory is null)
        {
            return Result.Fail($"No persisted memory exists for id {memoryId}.");
        }

        context.MemoryNodes.Remove(memory);
        await context.SaveChangesAsync(ct);

        return Result.Ok();
    }

    public async Task<IReadOnlyDictionary<int, MemoryNode[]>> GetNodeMemories(
        IReadOnlyCollection<int> nodeIds,
        IReadOnlyCollection<MemoryIntent>? intents,
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

        var normalizedIntents = NormalizeIntentFilter(intents);

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

        var memoriesQuery = context.MemoryNodes
            .AsNoTracking()
            .Where(memoryNode => normalizedNodeIds.Contains(memoryNode.TargetCodeNodeId));
        if (normalizedIntents.Length > 0)
        {
            var intentNames = normalizedIntents
                .Select(static i => i.ToString())
                .ToArray();
            memoriesQuery = memoriesQuery.Where(memoryNode => intentNames.Contains(memoryNode.Intent));
        }

        var memories = await memoriesQuery.ToArrayAsync(ct);
        var memoriesByNodeId = memories
            .OrderBy(memoryNode => memoryNode.CreatedAt)
            .GroupBy(static memoryNode => memoryNode.TargetCodeNodeId)
            .ToDictionary(
                static group => group.Key,
                group => group.ToArray());

        return codeNodes
            .ToDictionary(
                static codeNode => codeNode.Id,
                codeNode => memoriesByNodeId.TryGetValue(codeNode.Id, out var records)
                    ? records
                        .Select(record => Map(record, codeNode.FullyQualifiedName, codeNode.BodyHash ?? string.Empty))
                        .ToArray()
                    : []);
    }

    public async Task<MemoryNode?> GetMemory(Guid memoryId, CancellationToken ct)
    {
        if (memoryId == Guid.Empty)
        {
            return null;
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var record = await context.MemoryNodes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == memoryId, ct);
        if (record is null)
        {
            return null;
        }

        var targetNode = await context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => codeNode.Id == record.TargetCodeNodeId)
            .Select(static codeNode => new
            {
                codeNode.FullyQualifiedName,
                codeNode.BodyHash
            })
            .FirstOrDefaultAsync(ct);

        return targetNode is null ? null : Map(
            record,
            targetNode.FullyQualifiedName,
            targetNode.BodyHash ?? string.Empty);
    }

    public async Task<IReadOnlyDictionary<Guid, MemoryNode?>> GetMemories(
        IReadOnlyCollection<Guid> memoryIds,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(memoryIds);

        var normalizedIds = memoryIds
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        var result = memoryIds
            .Distinct()
            .ToDictionary(static id => id, static _ => (MemoryNode?)null);
        if (normalizedIds.Length == 0)
        {
            return result;
        }

        await using var context = await dbContextFactory.CreateDbContextAsync(ct);
        var records = await context.MemoryNodes
            .AsNoTracking()
            .Where(record => normalizedIds.Contains(record.Id))
            .ToArrayAsync(ct);
        if (records.Length == 0)
        {
            return result;
        }

        var nodeIds = records
            .Select(static record => record.TargetCodeNodeId)
            .Distinct()
            .ToArray();
        var nodesById = await context.CodeNodes
            .AsNoTracking()
            .Where(codeNode => nodeIds.Contains(codeNode.Id))
            .Select(static codeNode => new
            {
                codeNode.Id,
                codeNode.FullyQualifiedName,
                codeNode.BodyHash
            })
            .ToDictionaryAsync(static node => node.Id, ct);

        foreach (var record in records)
        {
            if (nodesById.TryGetValue(record.TargetCodeNodeId, out var targetNode))
            {
                result[record.Id] = Map(record, targetNode.FullyQualifiedName, targetNode.BodyHash ?? string.Empty);
            }
        }

        return result;
    }

    private static MemoryIntent[] NormalizeIntentFilter(IReadOnlyCollection<MemoryIntent>? intents)
    {
        if (intents is null || intents.Count == 0)
        {
            return [];
        }

        return intents
            .Select(NormalizeIntent)
            .Distinct()
            .ToArray();
    }

    private static MemoryNode Map(MemoryNodeRecord record, string fullyQualifiedName, string currentCodeHash)
        => new(
            record.Id,
            fullyQualifiedName,
            record.TargetCodeHash,
            record.Content,
            record.ContentHash,
            DeserializeTags(record.TagsJson),
            ParseIntent(record.Intent),
            record.CreatedAt,
            !string.Equals(currentCodeHash, record.TargetCodeHash, StringComparison.Ordinal));

    private static MemoryIntent NormalizeIntent(MemoryIntent intent)
        => Enum.IsDefined(typeof(MemoryIntent), intent) ? intent : MemoryIntent.Convention;

    private static MemoryIntent ParseIntent(string? raw)
        => Enum.TryParse<MemoryIntent>(raw, ignoreCase: true, out var parsed) ? parsed : MemoryIntent.Convention;

    private static string[] DeserializeTags(string tagsJson)
        => string.IsNullOrWhiteSpace(tagsJson)
            ? []
            : JsonSerializer.Deserialize<string[]>(tagsJson) ?? [];

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
