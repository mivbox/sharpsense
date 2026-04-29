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
    public async Task WhenGetGraphWithSelectedPath_ThenReturnsSelectedAndBoundaryNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);

        await SeedGraph(context);
        var service = new DependencyGraphRepository(context);

        var result = await service.GetGraph(["MyCompany.App"], true, CancellationToken.None);

        Assert.Contains(
            result.Nodes,
            static node => node is
                {
                    Id: "project:MyCompany.App/MyCompany.App.csproj",
                    Label: "MyCompany.App",
                    Type: "project",
                    Scope: "selected",
                    IsClickable: true
                });
        Assert.Contains(
            result.Nodes,
            static node => node is
                {
                    Id: "node-user-service",
                    Label: "MyCompany.App.UserService.LoadUser()",
                    Type: "method",
                    Scope: "selected",
                    IsClickable: true
                });
        Assert.Contains(
            result.Nodes,
            static node => node is
            {
                Id: "project:MyCompany.Core/MyCompany.Core.csproj",
                Label: "MyCompany.Core",
                Type: "project",
                Scope: "external",
                IsClickable: false
            });
        Assert.Contains(
            result.Nodes,
            static node => node is
            {
                Id: "node-user",
                Label: "MyCompany.Core.User",
                Type: "class",
                Scope: "external",
                IsClickable: false
            });

        Assert.Contains(
            result.Edges,
            static edge => edge is
            {
                Id:
                "project:MyCompany.App/MyCompany.App.csproj|project:MyCompany.Core/MyCompany.Core.csproj|projectreference",
                Source: "project:MyCompany.App/MyCompany.App.csproj",
                Target: "project:MyCompany.Core/MyCompany.Core.csproj",
                Type: "projectreference",
                Scope: "boundary"
            });
        Assert.Contains(
            result.Edges,
            static edge => edge is
            {
                Id: "node-user-service|node-user|methodcall",
                Source: "node-user-service",
                Target: "node-user",
                Type: "methodcall",
                Scope: "boundary"
            });
    }

    [Fact]
    public async Task WhenGetGraphContainsDanglingBoundaryEdges_ThenItFiltersThemOut()
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

        var result = await service.GetGraph(["MyCompany.App"], true, CancellationToken.None);

        Assert.DoesNotContain(result.Edges, static edge => edge.Target == "missing-node");
        Assert.Equal(3, result.Edges.Length);
    }

    [Fact]
    public async Task WhenGetGraphExcludesBoundaryNodes_ThenItReturnsOnlyInternalScope()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);

        await SeedGraph(context);
        var service = new DependencyGraphRepository(context);

        var result = await service.GetGraph(["MyCompany.App"], false, CancellationToken.None);

        Assert.Contains(
            result.Nodes,
            static node => node is
            {
                Id: "node-app-helper",
                Scope: "selected",
                IsClickable: true
            });
        Assert.DoesNotContain(result.Nodes, static node => node.Scope == "external");
        Assert.Contains(
            result.Edges,
            static edge => edge is
            {
                Id: "node-user-service|node-app-helper|methodcall",
                Source: "node-user-service",
                Target: "node-app-helper",
                Type: "methodcall",
                Scope: "internal"
            });
        Assert.DoesNotContain(result.Edges, static edge => edge.Scope == "boundary");
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
            },
            new CodeNode
            {
                Id = 3,
                CanonicalId = "node-app-helper",
                ProjectId = "project:MyCompany.App/MyCompany.App.csproj",
                FullyQualifiedName = "MyCompany.App.AppHelper.GetValue()",
                DisplayName = "AppHelper.GetValue()",
                NodeType = NodeType.Method,
                RelativeFilePath = "MyCompany.App/AppHelper.cs",
                Summary = "App helper."
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
            },
            new DependencyEdge
            {
                CallerId = "node-user-service", CalleeId = "node-app-helper", EdgeType = EdgeType.MethodCall
            });

        await db.SaveChangesAsync();
    }
}
