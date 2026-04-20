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
    [Fact]
    public async Task WhenTraceCallerDirectionRuns_ThenOutputsUpstreamNodesAsJson()
    {
        await using var database = await CliCommandTestDatabase.CreateAsync();
        using var console = new TestConsole();
        var app = SharpSense.Cli.Program.CreateCommandApp(console, database.ConfigureServices);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId, "--direction", "caller"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);

        using var jsonDocument = JsonDocument.Parse(console.Output);
        var nodes = jsonDocument.RootElement;
        nodes.ValueKind.Should().Be(JsonValueKind.Array);
        nodes.GetArrayLength().Should().Be(1);

        var node = nodes[0];
        node.GetProperty("id").GetString().Should().Be(CliCommandTestDatabase.CallerNodeId);
        node.GetProperty("fullyQualifiedName").GetString().Should().Be("Fixture.App.HttpEndpoint.Handle()");
        node.GetProperty("startLine").GetInt32().Should().Be(5);
        node.GetProperty("endLine").GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task WhenTraceCalleeDirectionRunsWithToon_ThenOutputsDownstreamNodesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.CreateAsync();
        using var console = new TestConsole();
        var app = SharpSense.Cli.Program.CreateCommandApp(console, database.ConfigureServices);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId, "--direction", "callee", "--toon"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[M] {CliCommandTestDatabase.CalleeNodeId} @ src/Fixture.App/MessageProvider.cs:7-11");
    }

    [Fact]
    public async Task WhenSearchRunsWithToon_ThenOutputsMatchingNodesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.CreateAsync();
        using var console = new TestConsole();
        var app = SharpSense.Cli.Program.CreateCommandApp(console, database.ConfigureServices);

        var exitCode = await app.RunAsync(
            ["search", "MessageProvider", "--toon"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[M] {CliCommandTestDatabase.CalleeNodeId} @ src/Fixture.App/MessageProvider.cs:7-11");
    }

    private sealed class CliCommandTestDatabase(InMemoryContextFactory contextFactory) : IAsyncDisposable
    {
        public const string ProjectId = "project-app";
        public const string SeedNodeId = "code:project-app:Fixture.App.MessageConsumer.Render()";
        public const string CallerNodeId = "code:project-app:Fixture.App.HttpEndpoint.Handle()";
        public const string CalleeNodeId = "code:project-app:Fixture.App.MessageProvider.GetMessage()";

        public static async Task<CliCommandTestDatabase> CreateAsync()
        {
            var contextFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
            var database = new CliCommandTestDatabase(contextFactory);
            await database.InitializeAsync();
            return database;
        }

        public Action<IServiceCollection> ConfigureServices => services =>
        {
            services.RemoveAll<IHostedService>();
            contextFactory.ConfigureServices<SharpSenseDbContext>(services);
        };

        public async ValueTask DisposeAsync()
        {
            await contextFactory.DisposeAsync();
        }

        private async Task InitializeAsync()
        {
            await using var dbContext = await contextFactory.GetContextAsync<SharpSenseDbContext>(
                ct: TestContext.Current.CancellationToken);

            dbContext.CodeNodes.AddRange(
                new CodeNode
                {
                    Id = SeedNodeId,
                    ProjectId = ProjectId,
                    FullyQualifiedName = "Fixture.App.MessageConsumer.Render()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = "src/Fixture.App/MessageConsumer.cs",
                    StartLine = 20,
                    EndLine = 28,
                    Summary = "Renders the message."
                },
                new CodeNode
                {
                    Id = CallerNodeId,
                    ProjectId = ProjectId,
                    FullyQualifiedName = "Fixture.App.HttpEndpoint.Handle()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = "src/Fixture.App/HttpEndpoint.cs",
                    StartLine = 5,
                    EndLine = 12,
                    Summary = "Handles the HTTP endpoint."
                },
                new CodeNode
                {
                    Id = CalleeNodeId,
                    ProjectId = ProjectId,
                    FullyQualifiedName = "Fixture.App.MessageProvider.GetMessage()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = "src/Fixture.App/MessageProvider.cs",
                    StartLine = 7,
                    EndLine = 11,
                    Summary = "Gets a message."
                });

            dbContext.DependencyEdges.AddRange(
                new DependencyEdge
                {
                    CallerId = CallerNodeId,
                    CalleeId = SeedNodeId,
                    EdgeType = EdgeType.MethodCall
                },
                new DependencyEdge
                {
                    CallerId = SeedNodeId,
                    CalleeId = CalleeNodeId,
                    EdgeType = EdgeType.MethodCall
                });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM CodeNodeSearch;
                INSERT INTO CodeNodeSearch (Id, FullyQualifiedName, Summary, RelativeFilePath)
                SELECT Id, FullyQualifiedName, Summary, RelativeFilePath
                FROM CodeNodes;
                """,
                TestContext.Current.CancellationToken);
        }
    }
}
