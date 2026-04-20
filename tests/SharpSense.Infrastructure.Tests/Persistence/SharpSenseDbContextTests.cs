using Microsoft.EntityFrameworkCore;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Persistence;

public sealed class SharpSenseDbContextTests
{
    [Fact]
    public async Task WhenSaveChangesAsyncWithNormalizedRelativePaths_ThenPersistsEmbeddingsAndRelativePaths()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = inMemoryFactory.GetContext<SharpSenseDbContext>(_ => { });

        context.ProjectNodes.Add(new ProjectNode
        {
            Id = "project-1",
            Name = "SharpSense.Domain",
            RelativeFilePath = "src/SharpSense.Domain/SharpSense.Domain.csproj",
            ContentHash = "project-hash"
        });

        context.CodeNodes.Add(new CodeNode
        {
            Id = "code-node-1",
            ProjectId = "project-1",
            FullyQualifiedName = "SharpSense.Domain.KnowledgeGraph.CodeNode",
            NodeType = NodeType.Class,
            RelativeFilePath = "src/SharpSense.Domain/KnowledgeGraph/Nodes/CodeNode.cs",
            StartLine = 7,
            EndLine = 20,
            Summary = "Knowledge graph node.",
            VectorEmbedding = [1.25f, -0.5f, 0.875f]
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var persistedProject = await context.ProjectNodes.SingleAsync(
            project => project.Id == "project-1",
            cancellationToken: TestContext.Current.CancellationToken);
        var persistedNode = await context.CodeNodes.SingleAsync(
            codeNode => codeNode.Id == "code-node-1",
            cancellationToken: TestContext.Current.CancellationToken);
        var vectorEmbeddingProperty = context.Model
            .FindEntityType(typeof(CodeNode))!
            .FindProperty(nameof(CodeNode.VectorEmbedding))!;

        Assert.Equal("src/SharpSense.Domain/SharpSense.Domain.csproj", persistedProject.RelativeFilePath);
        Assert.Equal("src/SharpSense.Domain/KnowledgeGraph/Nodes/CodeNode.cs", persistedNode.RelativeFilePath);
        Assert.Equal(7, persistedNode.StartLine);
        Assert.Equal(20, persistedNode.EndLine);
        Assert.Equal(new[] { 1.25f, -0.5f, 0.875f }, persistedNode.VectorEmbedding);
        Assert.Equal("BLOB", vectorEmbeddingProperty.GetColumnType());
    }

    [Fact]
    public async Task WhenSaveChangesAsyncWithAbsolutePathValues_ThenDoesNotNormalizeThemInsideDbContext()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = inMemoryFactory.GetContext<SharpSenseDbContext>(_ => { });
        var absolutePath = Path.Combine(
            Path.GetTempPath(),
            "sharp-sense-db-context",
            "src",
            "SharpSense.Domain",
            "SharpSense.Domain.csproj");

        context.ProjectNodes.Add(new ProjectNode
        {
            Id = "project-1",
            Name = "SharpSense.Domain",
            RelativeFilePath = absolutePath,
            ContentHash = "project-hash"
        });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var persistedProject = await context.ProjectNodes.SingleAsync(
            project => project.Id == "project-1",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(absolutePath, persistedProject.RelativeFilePath);
    }

    [Fact]
    public async Task WhenCreateDbContextAsyncWithSharedInMemoryFactory_ThenSharesPersistedStateAcrossContexts()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        var factory = inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>();

        await using (var writeContext = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            writeContext.ProjectNodes.Add(new ProjectNode
            {
                Id = "project-1",
                Name = "SharpSense.Domain",
                RelativeFilePath = "src/SharpSense.Domain/SharpSense.Domain.csproj",
                ContentHash = "project-hash"
            });

            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        var persistedProject = await readContext.ProjectNodes.SingleAsync(
            project => project.Id == "project-1",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("SharpSense.Domain", persistedProject.Name);
        Assert.Equal("src/SharpSense.Domain/SharpSense.Domain.csproj", persistedProject.RelativeFilePath);
    }
}
