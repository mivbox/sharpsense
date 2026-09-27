using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.CodeAnalysis;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing;

public sealed class PersistenceRegressionTests
{
    [Theory]
    [InlineData("Renamed.csproj", "Widget.cs")]
    [InlineData("src/Moved.csproj", "src/Widget.cs")]
    public async Task WhenProjectPathChanges_ThenFullReindexRetainsCodeIdentityAndAuthoredMemory(
        string newProjectPath,
        string newSourcePath)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var project = new IndexedProject("project:App.csproj", "App", "App.csproj", "project-hash");
        var widget = Node("Sample.Widget", "Widget.cs") with
        {
            CanonicalId = "code:project:App.csproj:T:Sample.Widget",
            ProjectId = project.Id
        };
        var caller = Node("Sample.Caller", "Caller.cs") with
        {
            CanonicalId = "code:project:App.csproj:T:Sample.Caller",
            ProjectId = project.Id
        };
        await repository.ReplaceWorkspace(
            new(
                [project],
                [widget, caller],
                [new(caller.CanonicalId, widget.CanonicalId, EdgeType.MethodCall)],
                []),
            ct);

        await using var context = await factory.GetContext(ct);
        var originalIds = await context.CodeNodes.ToDictionaryAsync(node => node.FullyQualifiedName, node => node.Id, ct);
        var memory = Memory(originalIds[widget.FullyQualifiedName]);
        context.MemoryNodes.Add(memory);
        await context.SaveChangesAsync(ct);

        var renamedProject = project with
        {
            Id = $"project:{newProjectPath}",
            RelativeFilePath = newProjectPath
        };
        var movedWidget = widget with
        {
            CanonicalId = $"code:{renamedProject.Id}:T:Sample.Widget",
            ProjectId = renamedProject.Id,
            RelativeFilePath = newSourcePath
        };
        var movedCaller = caller with
        {
            CanonicalId = $"code:{renamedProject.Id}:T:Sample.Caller",
            ProjectId = renamedProject.Id
        };
        var updated = new ExtractedNodes(
            [renamedProject],
            [movedWidget, movedCaller],
            [new(movedCaller.CanonicalId, movedWidget.CanonicalId, EdgeType.MethodCall)],
            []);
        await repository.ReplaceWorkspace(updated, ct);
        context.ChangeTracker.Clear();

        (await context.CodeNodes.ToDictionaryAsync(node => node.FullyQualifiedName, node => node.Id, ct))
            .Should().BeEquivalentTo(originalIds);
        var retainedMemory = await context.MemoryNodes.SingleAsync(ct);
        retainedMemory.Id.Should().Be(memory.Id);
        retainedMemory.Content.Should().Be(memory.Content);
        retainedMemory.TargetCodeHash.Should().Be(memory.TargetCodeHash);
        var currentProjectId = await context.ProjectNodes.Select(node => node.Id)
            .SingleAsync(ct);
        (await context.CodeNodes.Select(node => node.ProjectNodeId)
            .ToArrayAsync(ct))
            .Should().OnlyContain(id => id == currentProjectId);
        (await context.GraphNodes.Select(node => node.CanonicalId)
            .ToArrayAsync(ct))
            .Should().BeEquivalentTo(renamedProject.Id, movedWidget.CanonicalId, movedCaller.CanonicalId);
        var edge = await context.DependencyEdges.SingleAsync(ct);
        edge.CallerNodeId.Should().Be(originalIds[caller.FullyQualifiedName]);
        edge.CalleeNodeId.Should().Be(originalIds[widget.FullyQualifiedName]);
        (await context.Database.SqlQuery<string>($"SELECT CanonicalId AS Value FROM CodeNodeSearch")
            .ToArrayAsync(ct))
            .Should().BeEquivalentTo(movedWidget.CanonicalId, movedCaller.CanonicalId);
        (await context.Documents.Select(document => document.RelativePath)
            .ToArrayAsync(ct))
            .Should().BeEquivalentTo(newProjectPath, newSourcePath, caller.RelativeFilePath);

        await repository.ReplaceWorkspace(updated, ct);
        (await context.MemoryNodes.CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task WhenFullReindexChangesAndMovesNodes_ThenPreservesMemoriesAndPrunesRemovedGraph()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var project = new IndexedProject("project:app", "App", "app/App.csproj", "project-hash");
        var obsoleteProject = new IndexedProject("project:old", "Old", "old/Old.csproj", "old-hash");
        var survivor = Node("survivor", "old/Survivor.cs") with
        {
            ProjectId = project.Id
        };
        var removed = Node("removed", "old/Removed.cs") with
        {
            ProjectId = obsoleteProject.Id
        };
        await repository.ReplaceWorkspace(new([project, obsoleteProject], [survivor, removed], [], []), ct);

        await using var context = await factory.GetContext(ct);
        var persisted = await context.CodeNodes.SingleAsync(node => node.FullyQualifiedName == survivor.FullyQualifiedName, ct);
        var originalId = persisted.Id;
        var projectId = persisted.ProjectNodeId;
        context.MemoryNodes.Add(Memory(originalId));
        await context.SaveChangesAsync(ct);

        var changed = survivor with
        {
            RelativeFilePath = "app/Survivor.cs",
            Summary = "changed",
            BodyHash = "changed-hash"
        };
        await repository.ReplaceWorkspace(
            new(
                [project with
                {
                    Name = "Renamed App",
                    ContentHash = "new-project-hash"
                }],
                [changed, Node("added", "app/Added.cs")],
                [],
                []),
            ct);
        context.ChangeTracker.Clear();

        var updated = await context.CodeNodes.SingleAsync(node => node.Id == originalId, ct);
        updated.BodyHash.Should().Be("changed-hash");
        updated.ProjectNodeId.Should().Be(projectId);
        (await context.MemoryNodes.CountAsync(ct)).Should().Be(1);
        (await context.ProjectNodes.SingleAsync(ct)).Name.Should().Be("Renamed App");
        (await context.CodeNodes.CountAsync(ct)).Should().Be(2);
        (await context.GraphNodes.CountAsync(ct)).Should().Be(3);
        (await context.Documents.Select(document => document.RelativePath)
            .ToArrayAsync(ct))
            .Should().BeEquivalentTo("app/App.csproj", "app/Survivor.cs", "app/Added.cs");
        (await context.Directories.Select(directory => directory.Path)
            .ToArrayAsync(ct))
            .Should().BeEquivalentTo("", "app");
        (await context.Database.SqlQuery<int>($"SELECT COUNT(*) AS Value FROM CodeNodeSearch")
            .SingleAsync(ct)).Should().Be(2);

        await repository.ReplaceWorkspace(new([], [], [], []), ct);
        (await context.CodeNodes.CountAsync(ct)).Should().Be(0);
        (await context.GraphNodes.CountAsync(ct)).Should().Be(0);
        (await context.MemoryNodes.CountAsync(ct)).Should().Be(0);
        (await context.Documents.CountAsync(ct)).Should().Be(0);
    }

    [Fact]
    public async Task WhenEditingAndDeletingPartialDeclarations_ThenPreservesSharedIdentityAndRemainingMembers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        var repository = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var a = DocumentId.CreateNewId(projectId);
        var b = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "App",
            "App",
            LanguageNames.CSharp,
            filePath: "/repo/App.csproj"))
            .AddMetadataReference(projectId, MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddDocument(a, "A.cs", SourceText.From("public partial class Shared { public void A() {} }"), filePath: "/repo/A.cs")
            .AddDocument(b, "B.cs", SourceText.From("public partial class Shared { public void B() {} }"), filePath: "/repo/B.cs");
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/App.csproj"] = new("<Project Sdk=\"Microsoft.NET.Sdk\" />")
        });
        var repositoryWorkspace = new RepositoryWorkspace("/repo", "/repo/index.db", fileSystem);
        var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);
        var initial = await engine.Extract(
            "/repo/App.csproj",
            solution,
            repositoryWorkspace,
            ct: ct);
        await repository.ReplaceWorkspace(Map(initial), ct);
        await using var context = await factory.GetContext(ct);
        var classId = await context.CodeNodes.Where(node => node.NodeType == NodeType.Class)
            .Select(node => node.Id)
            .SingleAsync(ct);
        context.MemoryNodes.Add(Memory(classId));
        await context.SaveChangesAsync(ct);

        solution = solution.WithDocumentText(b, SourceText.From("public partial class Shared { public void B() {} public void C() {} }"));
        var modified = await engine.Extract(
            "/repo/App.csproj",
            solution,
            repositoryWorkspace,
            ct: ct);
        await repository.ReplaceWorkspace(Map(modified), ct);
        (await repository.GetPersistedCodeNodes(ct)).Should().Contain(node => node.FullyQualifiedName == "Shared.A()");
        (await repository.GetPersistedCodeNodes(ct)).Should().Contain(node => node.FullyQualifiedName == "Shared.C()");
        (await repository.GetPersistedCodeNodes(ct)).Single(node => node.NodeType == NodeType.Class).RelativeFilePath.Should().Be("A.cs");
        (await context.CodeNodes.SingleAsync(node => node.Id == classId, ct)).NodeType.Should().Be(NodeType.Class);

        solution = solution.RemoveDocument(a);
        var deleted = await engine.Extract("/repo/App.csproj", solution, repositoryWorkspace, ct: ct);
        await repository.ReplaceWorkspace(Map(deleted), ct);
        context.ChangeTracker.Clear();
        (await context.CodeNodes.SingleAsync(node => node.Id == classId, ct)).NodeType.Should().Be(NodeType.Class);
        (await repository.GetPersistedCodeNodes(ct)).Should().NotContain(node => node.FullyQualifiedName == "Shared.A()");
        (await repository.GetPersistedCodeNodes(ct)).Single(node => node.NodeType == NodeType.Class).RelativeFilePath.Should().Be("B.cs");
        (await context.MemoryNodes.CountAsync(ct)).Should().Be(1);
    }

    private static IndexedCodeNode Node(string name, string path)
        => new(
            $"code:{name}",
            null,
            name,
            name,
            NodeType.Class,
            path,
            1,
            3,
            name,
            name,
            "original-hash");

    private static MemoryNodeRecord Memory(int nodeId)
        => new()
        {
            Id = Guid.NewGuid(),
            TargetCodeNodeId = nodeId,
            TargetCodeHash = "original-hash",
            Content = "Authored context",
            ContentHash = "memory-hash",
            TagsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static ExtractedNodes Map(KnowledgeGraphExtractionPayload payload)
        => new(
            [],
            payload.CodeNodes.Select(node => new IndexedCodeNode(
                node.CanonicalId,
                node.ProjectId,
                node.FullyQualifiedName,
                node.DisplayName,
                node.NodeType,
                node.RelativeFilePath,
                node.StartLine,
                node.EndLine,
                node.Summary,
                node.SearchText,
                node.BodyHash))
                .ToArray(),
            payload.Edges.Select(edge => new IndexedDependency(edge.CallerId, edge.CalleeId, edge.EdgeType))
                .ToArray(),
            []);
}
