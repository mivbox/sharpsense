using AwesomeAssertions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Infrastructure.WorkspaceExplorer;
using SharpSense.Testkit;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Tests.WorkspaceExplorer;

public sealed class WorkspaceRootScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenSelectingReturnedRootDirectory_ThenGraphIncludesRootFilesAndDescendants(bool includeNestedFiles)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
        var index = new KnowledgeGraphRepository(factory.CreateDbContextFactory());
        var project = new IndexedProject("project:App.csproj", "App", "App.csproj", "project-hash");
        var rootNode = Node("App.Program", "Program.cs", project.Id);
        var nodes = new List<IndexedCodeNode>
        {
            rootNode
        };
        if (includeNestedFiles)
        {
            nodes.Add(Node("App.Feature", "src/Feature.cs", project.Id));
            nodes.Add(Node("App.FeatureTests", "tests/FeatureTests.cs", project.Id));
        }

        await index.ReplaceWorkspace(new([project], nodes, [], []), ct);
        await using var context = await factory.GetContext(ct);
        var tree = new WorkspaceTreeRepository(context);
        var graph = new GraphPageRepository(context, new RepositoryWorkspace("/repo", "/workspace-home/fixture/index.db", new FileSystem()));

        var root = await tree.GetTree("/", ct);

        root.ParentDirectoryId.Should().NotBeNull();
        root.ParentDirectoryId.Should().BeGreaterThan(0);
        root.Nodes.Should().Contain(node => node.Path == "Program.cs" && !node.IsSelectable);
        root.Nodes.Should().Contain(node => node.Path == "App.csproj" && node.Kind == "project");
        root.Nodes.Count(node => node.Kind == "folder").Should().Be(includeNestedFiles ? 2 : 0);
        var rootGraph = (await graph.GetNodesPage(new([root.ParentDirectoryId!.Value]), ct)).Items;
        rootGraph.Select(node => node.Label).Should().BeEquivalentTo(
            nodes.Select(node => node.FullyQualifiedName)
                .Append(project.Name));
        rootGraph.Should().OnlyContain(node => node.Scope == "selected");
        rootGraph.Single(node => node.Label == rootNode.FullyQualifiedName).CodeNodeId.Should().BeGreaterThan(0);

        if (includeNestedFiles)
        {
            var sourceTree = await tree.GetTree("src", ct);
            var sourceFolder = root.Nodes.Single(node => node.Path == "src");
            sourceTree.ParentDirectoryId.Should().Be(sourceFolder.Id);
            var sourceGraph = (await graph.GetNodesPage(new([sourceTree.ParentDirectoryId!.Value]), ct)).Items;
            sourceGraph.Should().ContainSingle().Which.Label.Should().Be("App.Feature");
            sourceGraph.Should().OnlyContain(node => node.Scope == "selected");
        }
    }

    private static IndexedCodeNode Node(string name, string path, string projectId)
        => new(
            $"code:{name}",
            projectId,
            name,
            name,
            NodeType.Class,
            path,
            1,
            3,
            name,
            name,
            "source-hash");
}
