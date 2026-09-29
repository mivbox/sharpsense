using AwesomeAssertions;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Memory.DeleteMemory;
using SharpSense.Application.Memory.DeleteMemory.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.IntegrationTests.Memory;

/// <summary>
/// End-to-end coverage of the <c>DeleteMemory</c> handler — confirms the immutable memory lifecycle (attach
/// then delete) is wired through the same handler contract the MCP tool and CLI both call.
/// </summary>
public sealed class DeleteMemoryTests
{
    [Fact]
    public async Task WhenDeleteMemoryInvokedWithExistingId_ThenMemoryIsRemoved()
    {
        await using var factory = new TestSharpSenseDbContextFactory();
        var dbContext = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        var store = new MemoryStore(factory, new NoopEmbeddingGenerator());

        const int nodeId = 1;
        const string fqdn = "Sample.Namespace.Greeter";
        dbContext.Directories.Add(new DirectoryRecord
        {
            Id = 1,
            Path = "Sample",
            Name = "Sample"
        });
        dbContext.Documents.Add(new DocumentRecord
        {
            Id = 1,
            DirectoryId = 1,
            FileName = "Greeter.cs",
            Extension = ".cs",
            RelativePath = "Sample/Greeter.cs",
            Kind = DocumentKind.Source
        });
        dbContext.GraphNodes.Add(new GraphNodeRecord
        {
            Id = nodeId,
            CanonicalId = fqdn,
            Kind = GraphNodeKind.Code
        });
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
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var attach = await store.AttachMemory(
            nodeId,
            "Always greet politely.",
            ["convention"],
            MemoryIntent.Convention,
            CancellationToken.None);
        attach.IsSuccess.Should().BeTrue();

        var reader = (IMemoryRepository)store;
        var before = await reader.GetNodeMemories([nodeId], intents: null, CancellationToken.None);
        var memoryId = before[nodeId][0].Id;

        var handler = new DeleteMemoryCommandHandler(store);
        var result = await handler.Handle(new DeleteMemoryCommand(memoryId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var after = await reader.GetNodeMemories([nodeId], intents: null, CancellationToken.None);
        after.Should().ContainKey(nodeId);
        after[nodeId].Should().BeEmpty("delete must remove the memory by id");
    }

    [Fact]
    public async Task WhenDeleteMemoryInvokedWithUnknownId_ThenReturnsFailedResult()
    {
        await using var factory = new TestSharpSenseDbContextFactory();
        var store = new MemoryStore(factory, new NoopEmbeddingGenerator());
        var handler = new DeleteMemoryCommandHandler(store);

        var result = await handler.Handle(new DeleteMemoryCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }
}
