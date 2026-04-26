using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.DependencyGraph;

public sealed class DependencyGraphRepositoryTests
{
    [Fact]
    public async Task WhenGetGraphWithSeededNodesAndEdges_ThenReturnsProjectedNodesAndEdges()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);

        await SeedGraph(context);
        var service = new DependencyGraphRepository(context);

        var result = await service.GetGraph(CancellationToken.None);

        Assert.Contains(
            result.Nodes,
            static node => node is
                { Id: "project:MyCompany.App/MyCompany.App.csproj", Label: "MyCompany.App", Type: "project" });
        Assert.Contains(
            result.Nodes,
            static node => node is
                { Id: "node-user-service", Label: "MyCompany.App.UserService.LoadUser()", Type: "method" });
        Assert.Contains(
            result.Nodes,
            static node => node is { Id: "node-user", Label: "MyCompany.Core.User", Type: "class" });

        Assert.Contains(
            result.Edges,
            static edge => edge is
            {
                Id:
                "project:MyCompany.App/MyCompany.App.csproj|project:MyCompany.Core/MyCompany.Core.csproj|projectreference",
                Source: "project:MyCompany.App/MyCompany.App.csproj",
                Target: "project:MyCompany.Core/MyCompany.Core.csproj", Type: "projectreference"
            });
        Assert.Contains(
            result.Edges,
            static edge => edge is
            {
                Id: "node-user-service|node-user|methodcall", Source: "node-user-service", Target: "node-user",
                Type: "methodcall"
            });
    }

    [Fact]
    public async Task WhenGetGraphContainsDanglingEdges_ThenItFiltersThemOut()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);

        await SeedGraph(context);
        context.DependencyEdges.Add(
            new DependencyEdge
            {
                CallerId = "node-user-service",
                CalleeId = "missing-node",
                EdgeType = EdgeType.MethodCall
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new DependencyGraphRepository(context);

        var result = await service.GetGraph(CancellationToken.None);

        Assert.DoesNotContain(result.Edges, static edge => edge.Target == "missing-node");
        Assert.Equal(2, result.Edges.Length);
    }

    private static async Task SeedGraph(SharpSenseDbContext db)
    {
        db.ProjectNodes.AddRange(
            new ProjectNode
            {
                Id = "project:MyCompany.App/MyCompany.App.csproj",
                Name = "MyCompany.App",
                RelativeFilePath = "MyCompany.App/MyCompany.App.csproj",
                ContentHash = "project-app"
            },
            new ProjectNode
            {
                Id = "project:MyCompany.Core/MyCompany.Core.csproj",
                Name = "MyCompany.Core",
                RelativeFilePath = "MyCompany.Core/MyCompany.Core.csproj",
                ContentHash = "project-core"
            });

        db.CodeNodes.AddRange(
            new CodeNode
            {
                Id = 1,
                CanonicalId = "node-user",
                ProjectId = "project:MyCompany.Core/MyCompany.Core.csproj",
                FullyQualifiedName = "MyCompany.Core.User",
                DisplayName = "User",
                NodeType = NodeType.Class,
                RelativeFilePath = "MyCompany.Core/User.cs",
                Summary = "User model."
            },
            new CodeNode
            {
                Id = 2,
                CanonicalId = "node-user-service",
                ProjectId = "project:MyCompany.App/MyCompany.App.csproj",
                FullyQualifiedName = "MyCompany.App.UserService.LoadUser()",
                DisplayName = "UserService.LoadUser()",
                NodeType = NodeType.Method,
                RelativeFilePath = "MyCompany.App/UserService.cs",
                Summary = "Loads users."
            });

        db.DependencyEdges.AddRange(
            new DependencyEdge
            {
                CallerId = "project:MyCompany.App/MyCompany.App.csproj",
                CalleeId = "project:MyCompany.Core/MyCompany.Core.csproj",
                EdgeType = EdgeType.ProjectReference
            },
            new DependencyEdge
            {
                CallerId = "node-user-service", CalleeId = "node-user", EdgeType = EdgeType.MethodCall
            });

        await db.SaveChangesAsync();
    }
}
