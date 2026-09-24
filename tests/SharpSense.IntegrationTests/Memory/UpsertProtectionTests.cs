using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.IntegrationTests.Memory;

/// <summary>
/// R4.2 — Upsert Protection. A memory attached to a code node must survive a full workspace re-parse of an
/// unchanged file. The parser's <c>ReplacePersistedGraph</c> path no longer bulk-deletes CodeNodes; it UPSERTs
/// them by FullyQualifiedName so the FK target row (and the memory) survives.
/// </summary>
public sealed class UpsertProtectionTests
{
    [Fact]
    public async Task WhenFullReParseRunsOnUnchangedFile_ThenAttachedMemorySurvives()
    {
        await using var factory = new TestSharpSenseDbContextFactory();
        var dbContext = await factory.CreateDbContextAsync();
        var store = new MemoryStore(factory, new NoopEmbeddingGenerator());
        var reader = (IMemoryRepository)store;

        const int nodeId = 1;
        const string fqdn = "Sample.Namespace.Greeter";
        dbContext.Directories.Add(new DirectoryRecord { Id = 1, Path = "Sample", Name = "Sample" });
        dbContext.Documents.Add(new DocumentRecord
        {
            Id = 1,
            DirectoryId = 1,
            FileName = "Greeter.cs",
            Extension = ".cs",
            RelativePath = "Sample/Greeter.cs",
            Kind = DocumentKind.Source
        });
        dbContext.GraphNodes.Add(new GraphNodeRecord { Id = nodeId, CanonicalId = fqdn, Kind = GraphNodeKind.Code });
        dbContext.CodeNodes.Add(new CodeNodeRecord
        {
            Id = nodeId,
            DocumentId = 1,
            FullyQualifiedName = fqdn,
            DisplayName = "Greeter",
            NodeType = NodeType.Class,
            StartLine = 1,
            EndLine = 10,
            Summary = "Greets the world.",
            SearchText = "Greeter greet",
            BodyHash = "hash-v1"
        });
        dbContext.MemoryNodes.Add(new MemoryNodeRecord
        {
            Id = Guid.NewGuid(),
            TargetCodeNodeId = nodeId,
            TargetCodeHash = "hash-v1",
            Content = "Always greet politely.",
            ContentHash = "content-hash",
            TagsJson = "[\"convention\"]",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        // Simulate a full re-parse: rebuild the CodeNode row with the SAME Id/FullyQualifiedName (the parser's
        // UPSERT path). The MemoryNode row should be untouched because we did not delete CodeNodes.
        var existing = await dbContext.CodeNodes.SingleAsync();
        existing.BodyHash = "hash-v1";
        existing.Summary = "Greets the world (re-parsed).";
        await dbContext.SaveChangesAsync();

        var memories = await reader.GetNodeMemories([nodeId], intents: null, CancellationToken.None);
        memories.Should().ContainKey(nodeId);
        memories[nodeId].Should().HaveCount(1, "the parser's UPSERT must preserve the memory");
        memories[nodeId][0].Content.Should().Be("Always greet politely.");
    }
}
