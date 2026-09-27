using AwesomeAssertions;
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
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = inMemoryFactory.GetContext(_ =>
        {
        });

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

        persistedProjectDocument.RelativePath.Should().Be("src/SharpSense.Domain/SharpSense.Domain.csproj");
        persistedNodeDocument.RelativePath.Should().Be("src/SharpSense.Domain/KnowledgeGraph/Nodes/CodeNode.cs");
        persistedProject.Id.Should().Be(100);
        persistedNode.StartLine.Should().Be(7);
        persistedNode.EndLine.Should().Be(20);
        persistedNode.VectorEmbedding.Should().Equal(new[]
        {
            1.25f,
            -0.5f,
            0.875f
        });
        vectorEmbeddingProperty.GetColumnType().Should().Be("BLOB");
    }

    [Fact]
    public async Task WhenSaveChangesAsyncWithAbsolutePathValues_ThenDoesNotNormalizeThemInsideDbContext()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = inMemoryFactory.GetContext(_ =>
        {
        });
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

        persistedProjectDocument.RelativePath.Should().Be(absolutePath);
    }

    [Fact]
    public async Task WhenCreateDbContextAsyncWithSharedInMemoryFactory_ThenSharesPersistedStateAcrossContexts()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        var factory = inMemoryFactory.CreateDbContextFactory();

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

        persistedProject.Name.Should().Be("SharpSense.Domain");
        persistedProjectDocument.RelativePath.Should().Be("src/SharpSense.Domain/SharpSense.Domain.csproj");
    }

    [Fact]
    public async Task WhenSaveChangesAsyncWithNullProjectNodeId_ThenPersistsDocumentNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = inMemoryFactory.GetContext(_ =>
        {
        });

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

        persistedNode.ProjectNodeId.Should().BeNull();
        persistedNode.NodeType.Should().Be(NodeType.Document);
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
