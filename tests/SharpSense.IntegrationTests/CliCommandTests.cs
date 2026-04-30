using System.Globalization;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Testkit;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class CliCommandTests
{
    private const string RepositoryRoot = "/repo";

    [Fact]
    public async Task WhenTraceCallerDirectionRuns_ThenOutputsUpstreamNodesAsJson()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--direction", "caller", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);

        using var jsonDocument = JsonDocument.Parse(console.Output);
        var nodes = jsonDocument.RootElement;
        nodes.ValueKind.Should().Be(JsonValueKind.Array);
        nodes.GetArrayLength().Should().Be(1);

        var node = nodes[0];
        node.GetProperty("id").GetInt32().Should().Be(CliCommandTestDatabase.CallerNodeId);
        node.GetProperty("canonicalId").GetString().Should().Be(CliCommandTestDatabase.CallerCanonicalId);
        node.GetProperty("fullyQualifiedName").GetString().Should().Be("Fixture.App.HttpEndpoint.Handle()");
        node.GetProperty("displayName").GetString().Should().Be("HttpEndpoint.Handle()");
        node.GetProperty("startLine").GetInt32().Should().Be(5);
        node.GetProperty("endLine").GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task WhenTraceCalleeDirectionRunsWithToon_ThenOutputsDownstreamNodesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--direction", "callee", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[M] `{CliCommandTestDatabase.CalleeNodeId}` MessageProvider.GetMessage() @ src/Fixture.App/MessageProvider.cs:7-11");
    }

    [Fact]
    public async Task WhenTraceRunsWithoutDirection_ThenItUsesCalleeTraversalByDefault()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[M] `{CliCommandTestDatabase.CalleeNodeId}` MessageProvider.GetMessage() @ src/Fixture.App/MessageProvider.cs:7-11");
    }

    [Fact]
    public async Task WhenSearchRunsWithToon_ThenOutputsMatchingNodesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["search", "MessageProvider", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[M] `{CliCommandTestDatabase.CalleeNodeId}` MessageProvider.GetMessage() @ src/Fixture.App/MessageProvider.cs:7-11");
    }

    [Fact]
    public async Task WhenSearchRunsWithToonForDocumentNode_ThenOutputsDocumentNodesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["search", "Getting Started", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[D] `{CliCommandTestDatabase.DocumentNodeId}` Guide#getting-started @ docs/Guide.md:1-3");
    }

    [Fact]
    public async Task WhenTraceCalleeDirectionRunsForDocumentRoot_ThenOutputsDownstreamDocumentNodesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.DocumentRootNodeId.ToString(CultureInfo.InvariantCulture), "--direction", "callee", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[D] `{CliCommandTestDatabase.LinkedDocumentRootNodeId}` Reference @ docs/Reference.md:1-1");
    }

    [Fact]
    public async Task WhenInheritorsRunsForClassNodeWithToon_ThenOutputsDerivedClassesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["inheritors", CliCommandTestDatabase.BaseClassNodeId.ToString(CultureInfo.InvariantCulture), "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[C] `{CliCommandTestDatabase.DerivedClassNodeId}` FancyRenderer @ src/Fixture.App/FancyRenderer.cs:3-18");
    }

    [Fact]
    public async Task WhenInheritorsRunsForInterfaceNodeWithToon_ThenOutputsImplementingClassesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["inheritors", CliCommandTestDatabase.InterfaceNodeId.ToString(CultureInfo.InvariantCulture), "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            $"[C] `{CliCommandTestDatabase.HtmlRendererNodeId}` HtmlRenderer @ src/Fixture.App/HtmlRenderer.cs:3-16" +
            Environment.NewLine +
            $"[C] `{CliCommandTestDatabase.TerminalRendererNodeId}` TerminalRenderer @ src/Fixture.App/TerminalRenderer.cs:3-15");
    }

    private static Spectre.Console.Cli.CommandApp CreateCommandApp(
        TestConsole console,
        CliCommandTestDatabase database)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);

        return Cli.Program.CreateCommandApp(
            console,
            services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                database.ConfigureServices(services);
            },
            enableFileLogging: false);
    }

    private sealed class CliCommandTestDatabase(InMemoryContextFactory contextFactory) : IAsyncDisposable
    {
        public const string ProjectId = "project-app";
        public const int SeedNodeId = 1;
        public const int CallerNodeId = 2;
        public const int CalleeNodeId = 3;
        public const int DocumentRootNodeId = 4;
        public const int LinkedDocumentRootNodeId = 5;
        public const int DocumentNodeId = 6;
        public const int InterfaceNodeId = 7;
        public const int HtmlRendererNodeId = 8;
        public const int TerminalRendererNodeId = 9;
        public const int BaseClassNodeId = 10;
        public const int DerivedClassNodeId = 11;
        public const string SeedCanonicalId = "code:project-app:Fixture.App.MessageConsumer.Render()";
        public const string CallerCanonicalId = "code:project-app:Fixture.App.HttpEndpoint.Handle()";
        public const string CalleeCanonicalId = "code:project-app:Fixture.App.MessageProvider.GetMessage()";
        public const string DocumentRootCanonicalId = "code:doc:docs/DocA.md#document-root";
        public const string LinkedDocumentRootCanonicalId = "code:doc:docs/Reference.md#document-root";
        public const string DocumentNodeCanonicalId = "code:doc:docs/Guide.md#getting-started";
        public const string InterfaceCanonicalId = "code:project-app:Fixture.App.IMessageRenderer";
        public const string HtmlRendererCanonicalId = "code:project-app:Fixture.App.HtmlRenderer";
        public const string TerminalRendererCanonicalId = "code:project-app:Fixture.App.TerminalRenderer";
        public const string BaseClassCanonicalId = "code:project-app:Fixture.App.BaseRenderer";
        public const string DerivedClassCanonicalId = "code:project-app:Fixture.App.FancyRenderer";

        public static async Task<CliCommandTestDatabase> Create()
        {
            var contextFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
            var database = new CliCommandTestDatabase(contextFactory);
            await database.Initialize();
            return database;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IHostedService>();
            contextFactory.ConfigureServices<SharpSenseDbContext>(services);
        }

        public async ValueTask DisposeAsync()
        {
            await contextFactory.DisposeAsync();
        }

        private async Task Initialize()
        {
            await using var dbContext = await contextFactory.GetContext<SharpSenseDbContext>(
                ct: TestContext.Current.CancellationToken);

            dbContext.Directories.AddRange(
                new DirectoryRecord
                {
                    Id = 1,
                    Path = string.Empty,
                    Name = "/"
                },
                new DirectoryRecord
                {
                    Id = 2,
                    ParentId = 1,
                    Path = "src",
                    Name = "src"
                },
                new DirectoryRecord
                {
                    Id = 3,
                    ParentId = 2,
                    Path = "src/Fixture.App",
                    Name = "Fixture.App"
                },
                new DirectoryRecord
                {
                    Id = 4,
                    ParentId = 1,
                    Path = "docs",
                    Name = "docs"
                });
            dbContext.DirectoryClosures.AddRange(
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 1,
                    DescendantDirectoryId = 1,
                    Depth = 0
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 2,
                    DescendantDirectoryId = 2,
                    Depth = 0
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 3,
                    DescendantDirectoryId = 3,
                    Depth = 0
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 4,
                    DescendantDirectoryId = 4,
                    Depth = 0
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 1,
                    DescendantDirectoryId = 2,
                    Depth = 1
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 1,
                    DescendantDirectoryId = 3,
                    Depth = 2
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 1,
                    DescendantDirectoryId = 4,
                    Depth = 1
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 2,
                    DescendantDirectoryId = 3,
                    Depth = 1
                });
            dbContext.Documents.AddRange(
                new DocumentRecord
                {
                    Id = 10,
                    DirectoryId = 3,
                    FileName = "Fixture.App.csproj",
                    Extension = ".csproj",
                    RelativePath = "src/Fixture.App/Fixture.App.csproj",
                    Kind = DocumentKind.ProjectFile
                },
                new DocumentRecord
                {
                    Id = 11,
                    DirectoryId = 3,
                    FileName = "MessageConsumer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/MessageConsumer.cs",
                    Kind = DocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 12,
                    DirectoryId = 3,
                    FileName = "HttpEndpoint.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/HttpEndpoint.cs",
                    Kind = DocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 13,
                    DirectoryId = 3,
                    FileName = "MessageProvider.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/MessageProvider.cs",
                    Kind = DocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 14,
                    DirectoryId = 4,
                    FileName = "DocA.md",
                    Extension = ".md",
                    RelativePath = "docs/DocA.md",
                    Kind = DocumentKind.Markdown
                },
                new DocumentRecord
                {
                    Id = 15,
                    DirectoryId = 4,
                    FileName = "Reference.md",
                    Extension = ".md",
                    RelativePath = "docs/Reference.md",
                    Kind = DocumentKind.Markdown
                },
                new DocumentRecord
                {
                    Id = 16,
                    DirectoryId = 4,
                    FileName = "Guide.md",
                    Extension = ".md",
                    RelativePath = "docs/Guide.md",
                    Kind = DocumentKind.Markdown
                },
                new DocumentRecord
                {
                    Id = 17,
                    DirectoryId = 3,
                    FileName = "IMessageRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/IMessageRenderer.cs",
                    Kind = DocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 18,
                    DirectoryId = 3,
                    FileName = "HtmlRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/HtmlRenderer.cs",
                    Kind = DocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 19,
                    DirectoryId = 3,
                    FileName = "TerminalRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/TerminalRenderer.cs",
                    Kind = DocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 20,
                    DirectoryId = 3,
                    FileName = "BaseRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/BaseRenderer.cs",
                    Kind = DocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 21,
                    DirectoryId = 3,
                    FileName = "FancyRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/FancyRenderer.cs",
                    Kind = DocumentKind.Source
                });
            dbContext.GraphNodes.AddRange(
                new GraphNodeRecord
                {
                    Id = 100,
                    CanonicalId = ProjectId,
                    Kind = GraphNodeKind.Project
                },
                new GraphNodeRecord
                {
                    Id = SeedNodeId,
                    CanonicalId = SeedCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = CallerNodeId,
                    CanonicalId = CallerCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = CalleeNodeId,
                    CanonicalId = CalleeCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = DocumentRootNodeId,
                    CanonicalId = DocumentRootCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = LinkedDocumentRootNodeId,
                    CanonicalId = LinkedDocumentRootCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = DocumentNodeId,
                    CanonicalId = DocumentNodeCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = InterfaceNodeId,
                    CanonicalId = InterfaceCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = HtmlRendererNodeId,
                    CanonicalId = HtmlRendererCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = TerminalRendererNodeId,
                    CanonicalId = TerminalRendererCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = BaseClassNodeId,
                    CanonicalId = BaseClassCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = DerivedClassNodeId,
                    CanonicalId = DerivedClassCanonicalId,
                    Kind = GraphNodeKind.Code
                });
            dbContext.ProjectNodes.Add(
                new ProjectNodeRecord
                {
                    Id = 100,
                    Name = "Fixture.App",
                    ProjectDocumentId = 10,
                    ContentHash = "fixture-app"
                });
            dbContext.CodeNodes.AddRange(
                new CodeNodeRecord
                {
                    Id = SeedNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 11,
                    FullyQualifiedName = "Fixture.App.MessageConsumer.Render()",
                    DisplayName = "MessageConsumer.Render()",
                    NodeType = NodeType.Method,
                    StartLine = 20,
                    EndLine = 28,
                    Summary = "Renders the message."
                },
                new CodeNodeRecord
                {
                    Id = CallerNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 12,
                    FullyQualifiedName = "Fixture.App.HttpEndpoint.Handle()",
                    DisplayName = "HttpEndpoint.Handle()",
                    NodeType = NodeType.Method,
                    StartLine = 5,
                    EndLine = 12,
                    Summary = "Handles the HTTP endpoint."
                },
                new CodeNodeRecord
                {
                    Id = CalleeNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 13,
                    FullyQualifiedName = "Fixture.App.MessageProvider.GetMessage()",
                    DisplayName = "MessageProvider.GetMessage()",
                    NodeType = NodeType.Method,
                    StartLine = 7,
                    EndLine = 11,
                    Summary = "Gets a message."
                },
                new CodeNodeRecord
                {
                    Id = DocumentRootNodeId,
                    ProjectNodeId = null,
                    DocumentId = 14,
                    FullyQualifiedName = "docs/DocA.md#document-root",
                    DisplayName = "DocA",
                    NodeType = NodeType.Document,
                    StartLine = 1,
                    EndLine = 1,
                    Summary = "See [Reference](./Reference.md)."
                },
                new CodeNodeRecord
                {
                    Id = LinkedDocumentRootNodeId,
                    ProjectNodeId = null,
                    DocumentId = 15,
                    FullyQualifiedName = "docs/Reference.md#document-root",
                    DisplayName = "Reference",
                    NodeType = NodeType.Document,
                    StartLine = 1,
                    EndLine = 1,
                    Summary = "Reference document."
                },
                new CodeNodeRecord
                {
                    Id = DocumentNodeId,
                    ProjectNodeId = null,
                    DocumentId = 16,
                    FullyQualifiedName = "docs/Guide.md#getting-started",
                    DisplayName = "Guide#getting-started",
                    NodeType = NodeType.Document,
                    StartLine = 1,
                    EndLine = 3,
                    Summary = "Getting Started guide."
                },
                new CodeNodeRecord
                {
                    Id = InterfaceNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 17,
                    FullyQualifiedName = "Fixture.App.IMessageRenderer",
                    DisplayName = "IMessageRenderer",
                    NodeType = NodeType.Interface,
                    StartLine = 3,
                    EndLine = 8,
                    Summary = "Renderer contract."
                },
                new CodeNodeRecord
                {
                    Id = HtmlRendererNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 18,
                    FullyQualifiedName = "Fixture.App.HtmlRenderer",
                    DisplayName = "HtmlRenderer",
                    NodeType = NodeType.Class,
                    StartLine = 3,
                    EndLine = 16,
                    Summary = "HTML renderer."
                },
                new CodeNodeRecord
                {
                    Id = TerminalRendererNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 19,
                    FullyQualifiedName = "Fixture.App.TerminalRenderer",
                    DisplayName = "TerminalRenderer",
                    NodeType = NodeType.Class,
                    StartLine = 3,
                    EndLine = 15,
                    Summary = "Terminal renderer."
                },
                new CodeNodeRecord
                {
                    Id = BaseClassNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 20,
                    FullyQualifiedName = "Fixture.App.BaseRenderer",
                    DisplayName = "BaseRenderer",
                    NodeType = NodeType.Class,
                    StartLine = 3,
                    EndLine = 14,
                    Summary = "Base renderer."
                },
                new CodeNodeRecord
                {
                    Id = DerivedClassNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 21,
                    FullyQualifiedName = "Fixture.App.FancyRenderer",
                    DisplayName = "FancyRenderer",
                    NodeType = NodeType.Class,
                    StartLine = 3,
                    EndLine = 18,
                    Summary = "Fancy renderer."
                });

            dbContext.DependencyEdges.AddRange(
                new DependencyEdgeRecord
                {
                    CallerNodeId = CallerNodeId,
                    CalleeNodeId = SeedNodeId,
                    EdgeType = EdgeType.MethodCall
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = SeedNodeId,
                    CalleeNodeId = CalleeNodeId,
                    EdgeType = EdgeType.MethodCall
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = DocumentRootNodeId,
                    CalleeNodeId = LinkedDocumentRootNodeId,
                    EdgeType = EdgeType.DocumentLink
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = HtmlRendererNodeId,
                    CalleeNodeId = InterfaceNodeId,
                    EdgeType = EdgeType.Implements
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = TerminalRendererNodeId,
                    CalleeNodeId = InterfaceNodeId,
                    EdgeType = EdgeType.Implements
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = DerivedClassNodeId,
                    CalleeNodeId = BaseClassNodeId,
                    EdgeType = EdgeType.Implements
                });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM CodeNodeSearch;
                INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath)
                SELECT codeNode.Id, graphNode.CanonicalId, codeNode.DisplayName, codeNode.FullyQualifiedName, codeNode.Summary, document.RelativePath
                FROM CodeNodes AS codeNode
                INNER JOIN GraphNodes AS graphNode ON graphNode.Id = codeNode.Id
                INNER JOIN Documents AS document ON document.Id = codeNode.DocumentId;
                """,
                TestContext.Current.CancellationToken);
        }
    }
}
