using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Testkit;

namespace SharpSense.Infrastructure.Tests.DependencyGraph;

public sealed class DependencyGraphRepositoryTests
{
    private const int AppDirectoryId = 2;
    private const int CoreDirectoryId = 3;
    private const int AppProjectNodeId = 100;
    private const int CoreProjectNodeId = 101;
    private const int UserNodeId = 200;
    private const int UserServiceNodeId = 201;
    private const int AppHelperNodeId = 202;

    [Fact]
    public async Task WhenGetGraphWithSelectedDirectory_ThenReturnsSelectedAndBoundaryNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory();
        await using var context = await inMemoryFactory.GetContext<SharpSenseDbContext>(ct: TestContext.Current.CancellationToken);

        await SeedGraph(context);
        var service = new DependencyGraphRepository(context);

        var result = await service.GetGraph([AppDirectoryId], true, CancellationToken.None);

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
        context.GraphNodes.Add(
            new GraphNodeRecord
            {
                Id = 999,
                CanonicalId = "missing-node",
                Kind = GraphNodeKind.Code
            });
        context.DependencyEdges.Add(
            new DependencyEdgeRecord
            {
                CallerNodeId = UserServiceNodeId,
                CalleeNodeId = 999,
                EdgeType = EdgeType.MethodCall
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var service = new DependencyGraphRepository(context);

        var result = await service.GetGraph([AppDirectoryId], true, CancellationToken.None);

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

        var result = await service.GetGraph([AppDirectoryId], false, CancellationToken.None);

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
        db.Directories.AddRange(
            new DirectoryRecord
            {
                Id = 1,
                Path = string.Empty,
                Name = "/"
            },
            new DirectoryRecord
            {
                Id = AppDirectoryId,
                ParentId = 1,
                Path = "MyCompany.App",
                Name = "MyCompany.App"
            },
            new DirectoryRecord
            {
                Id = CoreDirectoryId,
                ParentId = 1,
                Path = "MyCompany.Core",
                Name = "MyCompany.Core"
            });
        db.DirectoryClosures.AddRange(
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = 1,
                DescendantDirectoryId = 1,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = AppDirectoryId,
                DescendantDirectoryId = AppDirectoryId,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = CoreDirectoryId,
                DescendantDirectoryId = CoreDirectoryId,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = 1,
                DescendantDirectoryId = AppDirectoryId,
                Depth = 1
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = 1,
                DescendantDirectoryId = CoreDirectoryId,
                Depth = 1
            });
        db.Documents.AddRange(
            new DocumentRecord
            {
                Id = 10,
                DirectoryId = AppDirectoryId,
                FileName = "MyCompany.App.csproj",
                Extension = ".csproj",
                RelativePath = "MyCompany.App/MyCompany.App.csproj",
                Kind = DocumentKind.ProjectFile
            },
            new DocumentRecord
            {
                Id = 11,
                DirectoryId = CoreDirectoryId,
                FileName = "MyCompany.Core.csproj",
                Extension = ".csproj",
                RelativePath = "MyCompany.Core/MyCompany.Core.csproj",
                Kind = DocumentKind.ProjectFile
            },
            new DocumentRecord
            {
                Id = 12,
                DirectoryId = CoreDirectoryId,
                FileName = "User.cs",
                Extension = ".cs",
                RelativePath = "MyCompany.Core/User.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = 13,
                DirectoryId = AppDirectoryId,
                FileName = "UserService.cs",
                Extension = ".cs",
                RelativePath = "MyCompany.App/UserService.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = 14,
                DirectoryId = AppDirectoryId,
                FileName = "AppHelper.cs",
                Extension = ".cs",
                RelativePath = "MyCompany.App/AppHelper.cs",
                Kind = DocumentKind.Source
            });
        db.GraphNodes.AddRange(
            new GraphNodeRecord
            {
                Id = AppProjectNodeId,
                CanonicalId = "project:MyCompany.App/MyCompany.App.csproj",
                Kind = GraphNodeKind.Project
            },
            new GraphNodeRecord
            {
                Id = CoreProjectNodeId,
                CanonicalId = "project:MyCompany.Core/MyCompany.Core.csproj",
                Kind = GraphNodeKind.Project
            },
            new GraphNodeRecord
            {
                Id = UserNodeId,
                CanonicalId = "node-user",
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = UserServiceNodeId,
                CanonicalId = "node-user-service",
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = AppHelperNodeId,
                CanonicalId = "node-app-helper",
                Kind = GraphNodeKind.Code
            });
        db.ProjectNodes.AddRange(
            new ProjectNodeRecord
            {
                Id = AppProjectNodeId,
                Name = "MyCompany.App",
                ProjectDocumentId = 10,
                ContentHash = "project-app"
            },
            new ProjectNodeRecord
            {
                Id = CoreProjectNodeId,
                Name = "MyCompany.Core",
                ProjectDocumentId = 11,
                ContentHash = "project-core"
            });

        db.CodeNodes.AddRange(
            new CodeNodeRecord
            {
                Id = UserNodeId,
                ProjectNodeId = CoreProjectNodeId,
                DocumentId = 12,
                FullyQualifiedName = "MyCompany.Core.User",
                DisplayName = "User",
                NodeType = NodeType.Class,
                Summary = "User model."
            },
            new CodeNodeRecord
            {
                Id = UserServiceNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 13,
                FullyQualifiedName = "MyCompany.App.UserService.LoadUser()",
                DisplayName = "UserService.LoadUser()",
                NodeType = NodeType.Method,
                Summary = "Loads users."
            },
            new CodeNodeRecord
            {
                Id = AppHelperNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 14,
                FullyQualifiedName = "MyCompany.App.AppHelper.GetValue()",
                DisplayName = "AppHelper.GetValue()",
                NodeType = NodeType.Method,
                Summary = "App helper."
            });

        db.DependencyEdges.AddRange(
            new DependencyEdgeRecord
            {
                CallerNodeId = AppProjectNodeId,
                CalleeNodeId = CoreProjectNodeId,
                EdgeType = EdgeType.ProjectReference
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = UserServiceNodeId,
                CalleeNodeId = UserNodeId,
                EdgeType = EdgeType.MethodCall
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = UserServiceNodeId,
                CalleeNodeId = AppHelperNodeId,
                EdgeType = EdgeType.MethodCall
            });

        await db.SaveChangesAsync();
    }
}
