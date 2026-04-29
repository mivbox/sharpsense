using Microsoft.EntityFrameworkCore;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.Persistence;

public sealed class SharpSenseDbContextTests
{
    [Fact]
    public async Task WhenSaveChangesAsyncWithNormalizedRelativePaths_ThenPersistsEmbeddingsAndRelativePaths()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = inMemoryFactory.GetContext<SharpSenseDbContext>(_ => { });

        SeedProjectAndCodeDocuments(context, "src/SharpSense.Domain/SharpSense.Domain.csproj");
        context.CodeNodes.Add(
            new CodeNodeRecord
            {
                Id = 101,
                ProjectNodeId = 100,
                DocumentId = 11,
                FullyQualifiedName = "SharpSense.Domain.KnowledgeGraph.CodeNode",
                DisplayName = "CodeNode",
                NodeType = NodeType.Class,
                StartLine = 7,
                EndLine = 20,
                Summary = "Knowledge graph node.",
                VectorEmbedding = [1.25f, -0.5f, 0.875f]
            });
        context.GraphNodes.Add(
            new GraphNodeRecord
            {
                Id = 101,
                CanonicalId = "code-node-1",
                Kind = GraphNodeKind.Code
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var persistedProject = await context.ProjectNodes.SingleAsync(
            project => project.Id == 100,
            cancellationToken: TestContext.Current.CancellationToken);
        var persistedProjectDocument = await context.Documents.SingleAsync(
            document => document.Id == 10,
            cancellationToken: TestContext.Current.CancellationToken);
        var persistedNode = await context.CodeNodes.SingleAsync(
            codeNode => codeNode.Id == 101,
            cancellationToken: TestContext.Current.CancellationToken);
        var persistedNodeDocument = await context.Documents.SingleAsync(
            document => document.Id == 11,
            cancellationToken: TestContext.Current.CancellationToken);
        var vectorEmbeddingProperty = context.Model
            .FindEntityType(typeof(CodeNodeRecord))!
            .FindProperty(nameof(CodeNodeRecord.VectorEmbedding))!;

        Assert.Equal("src/SharpSense.Domain/SharpSense.Domain.csproj", persistedProjectDocument.RelativePath);
        Assert.Equal("src/SharpSense.Domain/KnowledgeGraph/Nodes/CodeNode.cs", persistedNodeDocument.RelativePath);
        Assert.Equal(100, persistedProject.Id);
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

        SeedDirectories(context);
        context.Documents.Add(
            new DocumentRecord
            {
                Id = 10,
                DirectoryId = 1,
                FileName = "SharpSense.Domain.csproj",
                Extension = ".csproj",
                RelativePath = absolutePath,
                Kind = DocumentKind.ProjectFile
            });
        context.GraphNodes.Add(
            new GraphNodeRecord
            {
                Id = 100,
                CanonicalId = "project-1",
                Kind = GraphNodeKind.Project
            });
        context.ProjectNodes.Add(
            new ProjectNodeRecord
            {
                Id = 100,
                Name = "SharpSense.Domain",
                ProjectDocumentId = 10,
                ContentHash = "project-hash"
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var persistedProjectDocument = await context.Documents.SingleAsync(
            document => document.Id == 10,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(absolutePath, persistedProjectDocument.RelativePath);
    }

    [Fact]
    public async Task WhenCreateDbContextAsyncWithSharedInMemoryFactory_ThenSharesPersistedStateAcrossContexts()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        var factory = inMemoryFactory.CreateDbContextFactory<SharpSenseDbContext>();

        await using (var writeContext = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            SeedDirectories(writeContext);
            writeContext.Documents.Add(
                new DocumentRecord
                {
                    Id = 10,
                    DirectoryId = 1,
                    FileName = "SharpSense.Domain.csproj",
                    Extension = ".csproj",
                    RelativePath = "src/SharpSense.Domain/SharpSense.Domain.csproj",
                    Kind = DocumentKind.ProjectFile
                });
            writeContext.GraphNodes.Add(
                new GraphNodeRecord
                {
                    Id = 100,
                    CanonicalId = "project-1",
                    Kind = GraphNodeKind.Project
                });
            writeContext.ProjectNodes.Add(
                new ProjectNodeRecord
                {
                    Id = 100,
                    Name = "SharpSense.Domain",
                    ProjectDocumentId = 10,
                    ContentHash = "project-hash"
                });

            await writeContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var readContext = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);

        var persistedProject = await readContext.ProjectNodes.SingleAsync(
            project => project.Id == 100,
            cancellationToken: TestContext.Current.CancellationToken);
        var persistedProjectDocument = await readContext.Documents.SingleAsync(
            document => document.Id == 10,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("SharpSense.Domain", persistedProject.Name);
        Assert.Equal("src/SharpSense.Domain/SharpSense.Domain.csproj", persistedProjectDocument.RelativePath);
    }

    [Fact]
    public async Task WhenSaveChangesAsyncWithNullProjectNodeId_ThenPersistsDocumentNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = inMemoryFactory.GetContext<SharpSenseDbContext>(_ => { });

        SeedDirectories(context);
        context.Documents.Add(
            new DocumentRecord
            {
                Id = 20,
                DirectoryId = 2,
                FileName = "Guide.md",
                Extension = ".md",
                RelativePath = "docs/Guide.md",
                Kind = DocumentKind.Markdown
            });
        context.GraphNodes.Add(
            new GraphNodeRecord
            {
                Id = 200,
                CanonicalId = "code:doc:docs/Guide.md#getting-started",
                Kind = GraphNodeKind.Code
            });
        context.CodeNodes.Add(
            new CodeNodeRecord
            {
                Id = 200,
                ProjectNodeId = null,
                DocumentId = 20,
                FullyQualifiedName = "docs/Guide.md#getting-started",
                DisplayName = "Guide#getting-started",
                NodeType = NodeType.Document,
                StartLine = 1,
                EndLine = 2,
                Summary = "Getting Started"
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var persistedNode = await context.CodeNodes.SingleAsync(
            codeNode => codeNode.Id == 200,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(persistedNode.ProjectNodeId);
        Assert.Equal(NodeType.Document, persistedNode.NodeType);
    }

    private static void SeedProjectAndCodeDocuments(
        SharpSenseDbContext context,
        string projectPath)
    {
        SeedDirectories(context);
        context.Documents.AddRange(
            new DocumentRecord
            {
                Id = 10,
                DirectoryId = 1,
                FileName = "SharpSense.Domain.csproj",
                Extension = ".csproj",
                RelativePath = projectPath,
                Kind = DocumentKind.ProjectFile
            },
            new DocumentRecord
            {
                Id = 11,
                DirectoryId = 1,
                FileName = "CodeNode.cs",
                Extension = ".cs",
                RelativePath = "src/SharpSense.Domain/KnowledgeGraph/Nodes/CodeNode.cs",
                Kind = DocumentKind.Source
            });
        context.GraphNodes.Add(
            new GraphNodeRecord
            {
                Id = 100,
                CanonicalId = "project-1",
                Kind = GraphNodeKind.Project
            });
        context.ProjectNodes.Add(
            new ProjectNodeRecord
            {
                Id = 100,
                Name = "SharpSense.Domain",
                ProjectDocumentId = 10,
                ContentHash = "project-hash"
            });
    }

    private static void SeedDirectories(SharpSenseDbContext context)
    {
        context.Directories.AddRange(
            new DirectoryRecord
            {
                Id = 1,
                Path = "src/SharpSense.Domain",
                Name = "SharpSense.Domain"
            },
            new DirectoryRecord
            {
                Id = 2,
                Path = "docs",
                Name = "docs"
            });
    }
}
