using System.Globalization;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
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
        public const string SeedCanonicalId = "code:project-app:Fixture.App.MessageConsumer.Render()";
        public const string CallerCanonicalId = "code:project-app:Fixture.App.HttpEndpoint.Handle()";
        public const string CalleeCanonicalId = "code:project-app:Fixture.App.MessageProvider.GetMessage()";
        public const string DocumentRootCanonicalId = "code:doc:docs/DocA.md#document-root";
        public const string LinkedDocumentRootCanonicalId = "code:doc:docs/Reference.md#document-root";
        public const string DocumentNodeCanonicalId = "code:doc:docs/Guide.md#getting-started";

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

            dbContext.CodeNodes.AddRange(
                new CodeNode
                {
                    Id = SeedNodeId,
                    CanonicalId = SeedCanonicalId,
                    ProjectId = ProjectId,
                    FullyQualifiedName = "Fixture.App.MessageConsumer.Render()",
                    DisplayName = "MessageConsumer.Render()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = "src/Fixture.App/MessageConsumer.cs",
                    StartLine = 20,
                    EndLine = 28,
                    Summary = "Renders the message."
                },
                new CodeNode
                {
                    Id = CallerNodeId,
                    CanonicalId = CallerCanonicalId,
                    ProjectId = ProjectId,
                    FullyQualifiedName = "Fixture.App.HttpEndpoint.Handle()",
                    DisplayName = "HttpEndpoint.Handle()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = "src/Fixture.App/HttpEndpoint.cs",
                    StartLine = 5,
                    EndLine = 12,
                    Summary = "Handles the HTTP endpoint."
                },
                new CodeNode
                {
                    Id = CalleeNodeId,
                    CanonicalId = CalleeCanonicalId,
                    ProjectId = ProjectId,
                    FullyQualifiedName = "Fixture.App.MessageProvider.GetMessage()",
                    DisplayName = "MessageProvider.GetMessage()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = "src/Fixture.App/MessageProvider.cs",
                    StartLine = 7,
                    EndLine = 11,
                    Summary = "Gets a message."
                },
                new CodeNode
                {
                    Id = DocumentRootNodeId,
                    CanonicalId = DocumentRootCanonicalId,
                    ProjectId = null,
                    FullyQualifiedName = "docs/DocA.md#document-root",
                    DisplayName = "DocA",
                    NodeType = NodeType.Document,
                    RelativeFilePath = "docs/DocA.md",
                    StartLine = 1,
                    EndLine = 1,
                    Summary = "See [Reference](./Reference.md)."
                },
                new CodeNode
                {
                    Id = LinkedDocumentRootNodeId,
                    CanonicalId = LinkedDocumentRootCanonicalId,
                    ProjectId = null,
                    FullyQualifiedName = "docs/Reference.md#document-root",
                    DisplayName = "Reference",
                    NodeType = NodeType.Document,
                    RelativeFilePath = "docs/Reference.md",
                    StartLine = 1,
                    EndLine = 1,
                    Summary = "Reference document."
                },
                new CodeNode
                {
                    Id = DocumentNodeId,
                    CanonicalId = DocumentNodeCanonicalId,
                    ProjectId = null,
                    FullyQualifiedName = "docs/Guide.md#getting-started",
                    DisplayName = "Guide#getting-started",
                    NodeType = NodeType.Document,
                    RelativeFilePath = "docs/Guide.md",
                    StartLine = 1,
                    EndLine = 3,
                    Summary = "Getting Started guide."
                });

            dbContext.DependencyEdges.AddRange(
                new DependencyEdge
                {
                    CallerId = CallerCanonicalId,
                    CalleeId = SeedCanonicalId,
                    EdgeType = EdgeType.MethodCall
                },
                new DependencyEdge
                {
                    CallerId = SeedCanonicalId,
                    CalleeId = CalleeCanonicalId,
                    EdgeType = EdgeType.MethodCall
                },
                new DependencyEdge
                {
                    CallerId = DocumentRootCanonicalId,
                    CalleeId = LinkedDocumentRootCanonicalId,
                    EdgeType = EdgeType.DocumentLink
                });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM CodeNodeSearch;
                INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath)
                SELECT Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath
                FROM CodeNodes;
                """,
                TestContext.Current.CancellationToken);
        }
    }
}
