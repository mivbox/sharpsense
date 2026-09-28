using AwesomeAssertions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.DependencyGraph;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;
using System.IO.Abstractions;

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
    private const int UserRepositoryInterfaceNodeId = 203;
    private const int UserRepositoryImplementationNodeId = 204;

    [Fact]
    public async Task WhenGetGraphNodesWithSelectedDirectory_ThenReturnsSelectedAndBoundaryNodes()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct: TestContext.Current.CancellationToken);

        await SeedGraph(context);
        var service = Repository(context);

        var result = (await service.GetNodesPage(new([AppDirectoryId]), TestContext.Current.CancellationToken)).Items.ToArray();

        result.Where(static node => node is
        {
            Id: AppProjectNodeId,
            Label: "MyCompany.App",
            Type: "project",
            Scope: "selected",
            IsClickable: true
        }).Should().NotBeEmpty();
        result.Where(static node => node is
        {
            Id: UserServiceNodeId,
            Label: "MyCompany.App.UserService.LoadUser()",
            Type: "method",
            Scope: "selected",
            IsClickable: true
        }).Should().NotBeEmpty();
        result.Where(static node => node is
        {
            Id: CoreProjectNodeId,
            Label: "MyCompany.Core",
            Type: "project",
            Scope: "external",
            IsClickable: false
        }).Should().NotBeEmpty();
        result.Where(static node => node is
        {
            Id: UserNodeId,
            Label: "MyCompany.Core.User",
            Type: "class",
            Scope: "external",
            IsClickable: false
        }).Should().NotBeEmpty();
    }

    [Fact]
    public async Task WhenGetGraphEdgesWithSelectedDirectory_ThenReturnsInternalAndBoundaryEdges()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct: TestContext.Current.CancellationToken);

        await SeedGraph(context);
        var service = Repository(context);

        var result = (await service.GetEdgesPage(new([AppDirectoryId]), TestContext.Current.CancellationToken)).Items.ToArray();

        result.Where(static edge => edge is
        {
            Source: UserServiceNodeId,
            Target: AppHelperNodeId,
            Type: "methodcall",
            Scope: "internal"
        }).Should().NotBeEmpty();
        result.Where(static edge => edge is
        {
            Source: AppProjectNodeId,
            Target: CoreProjectNodeId,
            Type: "projectreference",
            Scope: "boundary"
        }).Should().NotBeEmpty();
        result.Where(static edge => edge is
        {
            Source: UserServiceNodeId,
            Target: UserNodeId,
            Type: "methodcall",
            Scope: "boundary"
        }).Should().NotBeEmpty();
    }

    [Fact]
    public async Task WhenGetGraphEdgesContainDanglingBoundaryEdges_ThenItFiltersThemOut()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct: TestContext.Current.CancellationToken);

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

        var service = Repository(context);

        var result = (await service.GetEdgesPage(new([AppDirectoryId]), TestContext.Current.CancellationToken)).Items.ToArray();

        result.Should().NotContain(static edge => edge.Target == 999);
        result.Length.Should().Be(3);
    }

    [Fact]
    public async Task WhenGetGraphContainsBoundaryImplementsEdge_ThenItReturnsInterfaceAndImplementer()
    {
        await using var inMemoryFactory = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options));
        await using var context = await inMemoryFactory.GetContext(ct: TestContext.Current.CancellationToken);

        await SeedGraph(context);
        await SeedBoundaryImplementsScenario(context);
        var service = Repository(context);

        var nodes = (await service.GetNodesPage(new([AppDirectoryId]), TestContext.Current.CancellationToken)).Items.ToArray();
        var edges = (await service.GetEdgesPage(new([AppDirectoryId]), TestContext.Current.CancellationToken)).Items.ToArray();

        nodes.Where(static node => node is
        {
            Id: UserRepositoryInterfaceNodeId,
            Label: "MyCompany.App.Abstractions.IUserRepository",
            Type: "interface",
            Scope: "selected",
            IsClickable: true
        }).Should().NotBeEmpty();
        nodes.Where(static node => node is
        {
            Id: UserRepositoryImplementationNodeId,
            Label: "MyCompany.Core.UserRepository",
            Type: "class",
            Scope: "external",
            IsClickable: false
        }).Should().NotBeEmpty();
        edges.Where(static edge => edge is
        {
            Source: UserRepositoryImplementationNodeId,
            Target: UserRepositoryInterfaceNodeId,
            Type: "implements",
            Scope: "boundary"
        }).Should().NotBeEmpty();
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

    private static async Task SeedBoundaryImplementsScenario(SharpSenseDbContext db)
    {
        db.Documents.AddRange(
            new DocumentRecord
            {
                Id = 18,
                DirectoryId = AppDirectoryId,
                FileName = "IUserRepository.cs",
                Extension = ".cs",
                RelativePath = "MyCompany.App/IUserRepository.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = 19,
                DirectoryId = CoreDirectoryId,
                FileName = "UserRepository.cs",
                Extension = ".cs",
                RelativePath = "MyCompany.Core/UserRepository.cs",
                Kind = DocumentKind.Source
            });
        db.GraphNodes.AddRange(
            new GraphNodeRecord
            {
                Id = UserRepositoryInterfaceNodeId,
                CanonicalId = "node-user-repository-interface",
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = UserRepositoryImplementationNodeId,
                CanonicalId = "node-user-repository-implementation",
                Kind = GraphNodeKind.Code
            });
        db.CodeNodes.AddRange(
            new CodeNodeRecord
            {
                Id = UserRepositoryInterfaceNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 18,
                FullyQualifiedName = "MyCompany.App.Abstractions.IUserRepository",
                DisplayName = "IUserRepository",
                NodeType = NodeType.Interface,
                Summary = "Application repository abstraction."
            },
            new CodeNodeRecord
            {
                Id = UserRepositoryImplementationNodeId,
                ProjectNodeId = CoreProjectNodeId,
                DocumentId = 19,
                FullyQualifiedName = "MyCompany.Core.UserRepository",
                DisplayName = "UserRepository",
                NodeType = NodeType.Class,
                Summary = "Repository implementation."
            });
        db.DependencyEdges.Add(
            new DependencyEdgeRecord
            {
                CallerNodeId = UserRepositoryImplementationNodeId,
                CalleeNodeId = UserRepositoryInterfaceNodeId,
                EdgeType = EdgeType.Implements
            });

        await db.SaveChangesAsync();
    }

    private static GraphPageRepository Repository(SharpSenseDbContext context)
        => new(context, new RepositoryWorkspace("/repo", "/workspace-home/fixture/index.db", new FileSystem()));
}
