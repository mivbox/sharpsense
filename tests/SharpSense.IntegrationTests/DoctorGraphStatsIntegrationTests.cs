using System.Text.Json;
using System.IO.Abstractions;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Moq;
using SharpSense.Application.GraphStats;
using SharpSense.Application.GraphStats.GetGraphStats;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Indexing;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Cli.Mcp;
using SharpSense.Cli.Ui.Api;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class DoctorGraphStatsIntegrationTests
{
    [Fact]
    public async Task DoctorJson_MissingDatabase_ReturnsReportWithoutCreatingAnIndex()
    {
        using var fixture = new Fixture();
        using var console = new TestConsole();
        var app = Cli.Program.CreateCommandApp(
            console,
            services => services.AddSingleton(fixture.Selection).AddSingleton(fixture.Workspace),
            enableFileLogging: false);

        var exitCode = await app.RunAsync(
            ["doctor", "--json", "--repo-root", fixture.Directory.FullName],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        using var document = JsonDocument.Parse(console.Output);
        var report = document.RootElement;
        report.GetProperty("status").GetString().Should().Be("warning");
        report.GetProperty("graph").GetProperty("databaseState").GetString().Should().Be("missing");
        report.GetProperty("graph").GetProperty("databasePath").GetString().Should().Be(fixture.Workspace.DatabasePath);
        report.GetProperty("checks").EnumerateArray().Should().Contain(check => check.GetProperty("code").GetString() == "typescript-runtime");
        Directory.GetFiles(fixture.Directory.FullName).Should().BeEmpty();
    }

    [Fact]
    public async Task DoctorJson_LegacyEnum_ReturnsActionableFailureWithoutChangingDatabase()
    {
        using var fixture = new Fixture();
        await fixture.CreateLegacyEnumDatabase();
        var before = await File.ReadAllBytesAsync(fixture.Workspace.DatabasePath, TestContext.Current.CancellationToken);
        using var console = new TestConsole();
        var app = Cli.Program.CreateCommandApp(
            console,
            services => services.AddSingleton(fixture.Selection).AddSingleton(fixture.Workspace),
            enableFileLogging: false);

        var exitCode = await app.RunAsync(
            ["doctor", "--json", "--repo-root", fixture.Directory.FullName],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        using var document = JsonDocument.Parse(console.Output);
        var report = document.RootElement;
        report.GetProperty("status").GetString().Should().Be("attention-required");
        var graph = report.GetProperty("graph");
        graph.GetProperty("databaseState").GetString().Should().Be("incompatible");
        graph.GetProperty("diagnostics").EnumerateArray().Should().Contain(diagnostic =>
            diagnostic.GetProperty("message").GetString()!.Contains("DocumentHierarchy") &&
            diagnostic.GetProperty("suggestion").GetString()!.Contains("authored memories"));
        (await File.ReadAllBytesAsync(fixture.Workspace.DatabasePath, TestContext.Current.CancellationToken)).Should().Equal(before);
    }

    [Fact]
    public async Task GraphStatsMcp_DeclaresReadOnlyZeroArgumentToolAndResolvesRealQuery()
    {
        using var fixture = new Fixture();
        var services = new ServiceCollection();
        fixture.AddGraphStats(services);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var method = typeof(SharpSenseMcpTools).GetMethod(nameof(SharpSenseMcpTools.graph_stats))!;
        var tool = McpServerTool.Create(method, target: null, options: new McpServerToolCreateOptions { Services = provider });

        tool.ProtocolTool.Name.Should().Be("graph_stats");
        tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Should().BeEmpty();
        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();

        var result = await SharpSenseMcpTools.graph_stats(
            provider.GetRequiredService<IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot>>(),
            TestContext.Current.CancellationToken);

        result.RepositoryRoot.Should().Be(fixture.Workspace.RootPath);
        result.DatabaseState.Should().Be("missing");
        Directory.GetFiles(fixture.Directory.FullName).Should().BeEmpty();
    }

    [Fact]
    public async Task GraphStatsHttp_ReturnsTypedCamelCaseSnapshotWithoutNodeInput()
    {
        using var fixture = new Fixture();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        fixture.AddGraphStats(builder.Services);
        await using var app = builder.Build();
        GraphStatsEndpoint.Map(app);
        await app.StartAsync(TestContext.Current.CancellationToken);

        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features
                .Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync("/api/tools/graph-stats", TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var graph = document.RootElement;

            graph.GetProperty("databaseState").GetString().Should().Be("missing");
            graph.GetProperty("isIndexed").GetBoolean().Should().BeFalse();
            graph.GetProperty("codeNodeCount").GetInt64().Should().Be(0);
            graph.GetProperty("lastSuccessfulIndex").ValueKind.Should().Be(JsonValueKind.Null);
            graph.GetProperty("languages").GetArrayLength().Should().Be(0);
            graph.GetProperty("diagnostics")[0].GetProperty("suggestion").GetString().Should().Contain("analyze");
            Directory.GetFiles(fixture.Directory.FullName).Should().BeEmpty();
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public DirectoryInfo Directory { get; } = System.IO.Directory.CreateTempSubdirectory("sharpsense-doctor-tests-");

        public IRepositoryWorkspace Workspace { get; }

        public WorkspaceSelection Selection { get; }

        public Fixture()
        {
            var catalog = new WorkspaceCatalog(new FileSystem(), Path.Combine(Directory.FullName, "home"));
            Selection = catalog.Create("fixture", Directory.FullName,
                [new WorkspaceSource(WorkspaceSourceKind.Markdown, "**/*.md")]);
            Workspace = Selection.Workspace;
        }

        public void AddGraphStats(IServiceCollection services)
        {
            services.AddSingleton(Workspace);
            services.AddGraphStats().AddGraphStatsInfrastructure();
        }

        public async Task CreateLegacyEnumDatabase()
        {
            await using var db = new SharpSenseDbContext(new DbContextOptionsBuilder<SharpSenseDbContext>()
                .UseSqlite($"Data Source={Workspace.DatabasePath};Pooling=False")
                .AddInterceptors(new SqlitePragmaInterceptor())
                .Options);
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO Directories (Id, ParentId, Path, Name) VALUES (1, NULL, '', '');
                INSERT INTO Documents (Id, DirectoryId, FileName, Extension, RelativePath, Kind)
                    VALUES (1, 1, 'Widget.cs', '.cs', 'Widget.cs', 'Source');
                INSERT INTO GraphNodes (Id, CanonicalId, Kind) VALUES (1, 'code:Widget', 'Code');
                INSERT INTO CodeNodes (Id, ProjectNodeId, DocumentId, FullyQualifiedName, DisplayName, NodeType,
                    StartLine, EndLine, Summary, SearchText, BodyHash, VectorEmbedding)
                    VALUES (1, NULL, 1, 'Widget', 'Widget', 'Class', 1, 3, 'Widget', 'Widget', 'body', NULL);
                INSERT INTO DependencyEdges (CallerNodeId, CalleeNodeId, EdgeType) VALUES (1, 1, 'DocumentHierarchy');
                INSERT INTO MemoryNodes (Id, TargetCodeNodeId, TargetCodeHash, Content, ContentHash,
                    TagsJson, Intent, VectorEmbedding, CreatedAt)
                    VALUES ('F11E5391-64A7-4F5A-B85D-6CC48B9F18C1', 1, 'body', 'Authored invariant',
                    'content-hash', '[]', 'Invariant', NULL, '2026-09-24 00:00:00+00:00');
                """, TestContext.Current.CancellationToken);
        }

        public void Dispose() => Directory.Delete(recursive: true);
    }
}
