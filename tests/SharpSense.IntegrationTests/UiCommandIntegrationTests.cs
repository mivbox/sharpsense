using System.Net;
using System.Net.Sockets;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.IntegrationTests;

public sealed class UiCommandIntegrationTests
{
    private const string RepositoryRoot = "/repo";
    private const string AppFolderPath = "src/Fixture.App";
    private const string CoreFolderPath = "src/Fixture.Core";
    private const string RootTreePath = "%2F";

    [Fact]
    public async Task WhenUiCommandRuns_ThenTreeAndScopedGraphEndpointsReturnAnalyzedData()
    {
        await using var database = await UiCommandTestDatabase.Create();
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                database.ConfigureServices(services);
            },
            enableFileLogging: false);
        using var shutdown = new CancellationTokenSource();
        var runTask = app.RunAsync(
            ["ui", "--url", baseUrl, "--repo-root", RepositoryRoot],
            shutdown.Token);

        try
        {
            await WaitForServer(runTask, $"{baseUrl}/");

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            using var graphResponse = await httpClient.GetAsync($"{baseUrl}/api/graph", TestContext.Current.CancellationToken);
            using var rootTreeResponse = await httpClient.GetAsync($"{baseUrl}/api/tree?path={RootTreePath}", TestContext.Current.CancellationToken);
            using var srcTreeResponse = await httpClient.GetAsync($"{baseUrl}/api/tree?path=src", TestContext.Current.CancellationToken);
            using var rootResponse = await httpClient.GetAsync($"{baseUrl}/", TestContext.Current.CancellationToken);
            using var scopedGraphResponse = await httpClient.GetAsync($"{baseUrl}/api/graph?paths={AppFolderPath}", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, graphResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, rootTreeResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, srcTreeResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, scopedGraphResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);

            await using var graphStream = await graphResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            await using var rootTreeStream = await rootTreeResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            await using var srcTreeStream = await srcTreeResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            await using var scopedGraphStream = await scopedGraphResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            using var jsonDocument = await JsonDocument.ParseAsync(graphStream, cancellationToken: TestContext.Current.CancellationToken);
            using var rootTreeDocument = await JsonDocument.ParseAsync(rootTreeStream, cancellationToken: TestContext.Current.CancellationToken);
            using var srcTreeDocument = await JsonDocument.ParseAsync(srcTreeStream, cancellationToken: TestContext.Current.CancellationToken);
            using var scopedGraphDocument = await JsonDocument.ParseAsync(scopedGraphStream, cancellationToken: TestContext.Current.CancellationToken);
            var nodes = jsonDocument.RootElement.GetProperty("nodes");
            var edges = jsonDocument.RootElement.GetProperty("edges");
            var rootTreeNodes = rootTreeDocument.RootElement.GetProperty("nodes");
            var srcTreeNodes = srcTreeDocument.RootElement.GetProperty("nodes");
            var scopedNodes = scopedGraphDocument.RootElement.GetProperty("nodes");
            var scopedEdges = scopedGraphDocument.RootElement.GetProperty("edges");

            Assert.Empty(nodes.EnumerateArray());
            Assert.Empty(edges.EnumerateArray());
            Assert.Contains(
                rootTreeNodes.EnumerateArray(),
                node =>
                    node.GetProperty("path").GetString() == "src" &&
                    node.GetProperty("kind").GetString() == "folder");
            Assert.Contains(
                srcTreeNodes.EnumerateArray(),
                node =>
                    node.GetProperty("path").GetString() == AppFolderPath &&
                    node.GetProperty("kind").GetString() == "folder");
            Assert.Contains(
                srcTreeNodes.EnumerateArray(),
                node =>
                    node.GetProperty("path").GetString() == CoreFolderPath &&
                    node.GetProperty("kind").GetString() == "folder");

            Assert.Contains(
                scopedNodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetString() == UiCommandTestDatabase.AppProjectId &&
                    node.GetProperty("scope").GetString() == "selected");
            Assert.Contains(
                scopedNodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetString() == UiCommandTestDatabase.CallerCanonicalId &&
                    node.GetProperty("label").GetString() == "Fixture.App.HttpEndpoint.Handle()" &&
                    node.GetProperty("type").GetString() == "method" &&
                    node.GetProperty("scope").GetString() == "selected");
            Assert.Contains(
                scopedNodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetString() == UiCommandTestDatabase.CoreProjectId &&
                    node.GetProperty("scope").GetString() == "external");
            Assert.Contains(
                scopedNodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetString() == UiCommandTestDatabase.CalleeCanonicalId &&
                    node.GetProperty("label").GetString() == "Fixture.Core.MessageProvider.GetMessage()" &&
                    node.GetProperty("type").GetString() == "method" &&
                    node.GetProperty("scope").GetString() == "external");
            Assert.Contains(
                scopedEdges.EnumerateArray(),
                edge =>
                    edge.GetProperty("source").GetString() == UiCommandTestDatabase.AppProjectId &&
                    edge.GetProperty("target").GetString() == UiCommandTestDatabase.CoreProjectId &&
                    edge.GetProperty("type").GetString() == "projectreference" &&
                    edge.GetProperty("scope").GetString() == "boundary");
            Assert.Contains(
                scopedEdges.EnumerateArray(),
                edge =>
                    edge.GetProperty("source").GetString() == UiCommandTestDatabase.CallerCanonicalId &&
                    edge.GetProperty("target").GetString() == UiCommandTestDatabase.CalleeCanonicalId &&
                    edge.GetProperty("type").GetString() == "methodcall" &&
                    edge.GetProperty("scope").GetString() == "boundary");

            shutdown.Cancel();
            Assert.Equal(0, await runTask);
        }
        finally
        {
            shutdown.Cancel();

            if (!runTask.IsCompleted)
            {
                await runTask;
            }
        }
    }

    private static async Task WaitForServer(Task<int> runTask, string url)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };

        var startedAtUtc = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAtUtc < TimeSpan.FromSeconds(30))
        {
            if (runTask.IsCompleted)
            {
                throw new InvalidOperationException(
                    $"The UI command exited before it became ready at '{url}'. Exit code: {await runTask}.");
            }

            try
            {
                using var response = await httpClient.GetAsync(url, TestContext.Current.CancellationToken);
                if (response.IsSuccessStatusCode && !runTask.IsCompleted)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"The UI command did not become ready at '{url}'.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class UiCommandTestDatabase(InMemoryContextFactory contextFactory) : IAsyncDisposable
    {
        public const string AppProjectId = "project:src/Fixture.App/Fixture.App.csproj";
        public const string CoreProjectId = "project:src/Fixture.Core/Fixture.Core.csproj";
        public const string CallerCanonicalId = "code:project:src/Fixture.App/Fixture.App.csproj:Fixture.App.HttpEndpoint.Handle()";
        public const string CalleeCanonicalId = "code:project:src/Fixture.Core/Fixture.Core.csproj:Fixture.Core.MessageProvider.GetMessage()";

        public static async Task<UiCommandTestDatabase> Create()
        {
            var contextFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(UseMigrations: true));
            var database = new UiCommandTestDatabase(contextFactory);
            await database.Initialize();
            return database;
        }

        public void ConfigureServices(IServiceCollection services)
            => contextFactory.ConfigureServices<SharpSenseDbContext>(services);

        public async ValueTask DisposeAsync()
        {
            await contextFactory.DisposeAsync();
        }

        private async Task Initialize()
        {
            await using var dbContext = await contextFactory.GetContext<SharpSenseDbContext>(
                ct: TestContext.Current.CancellationToken);

            dbContext.ProjectNodes.Add(new ProjectNode
            {
                Id = AppProjectId,
                Name = "Fixture.App",
                RelativeFilePath = $"{AppFolderPath}/Fixture.App.csproj",
                ContentHash = "project-hash"
            });
            dbContext.ProjectNodes.Add(new ProjectNode
            {
                Id = CoreProjectId,
                Name = "Fixture.Core",
                RelativeFilePath = $"{CoreFolderPath}/Fixture.Core.csproj",
                ContentHash = "project-core-hash"
            });

            dbContext.CodeNodes.AddRange(
                new CodeNode
                {
                    Id = 1,
                    CanonicalId = CallerCanonicalId,
                    ProjectId = AppProjectId,
                    FullyQualifiedName = "Fixture.App.HttpEndpoint.Handle()",
                    DisplayName = "HttpEndpoint.Handle()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = $"{AppFolderPath}/HttpEndpoint.cs",
                    StartLine = 5,
                    EndLine = 12,
                    Summary = "Handles the HTTP endpoint."
                },
                new CodeNode
                {
                    Id = 2,
                    CanonicalId = CalleeCanonicalId,
                    ProjectId = CoreProjectId,
                    FullyQualifiedName = "Fixture.Core.MessageProvider.GetMessage()",
                    DisplayName = "MessageProvider.GetMessage()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = $"{CoreFolderPath}/MessageProvider.cs",
                    StartLine = 7,
                    EndLine = 11,
                    Summary = "Gets a message."
                });

            dbContext.DependencyEdges.Add(new DependencyEdge
            {
                CallerId = AppProjectId,
                CalleeId = CoreProjectId,
                EdgeType = EdgeType.ProjectReference
            });
            dbContext.DependencyEdges.Add(new DependencyEdge
            {
                CallerId = CallerCanonicalId,
                CalleeId = CalleeCanonicalId,
                EdgeType = EdgeType.MethodCall
            });
            dbContext.WorkspaceTreeNodes.AddRange(
                new WorkspaceTreeNode
                {
                    Id = "src",
                    Path = "src",
                    Label = "src",
                    Kind = WorkspaceTreeNodeKind.Folder,
                    HasChildren = true,
                    ChildCount = 2,
                    IsSelectable = true
                },
                new WorkspaceTreeNode
                {
                    Id = AppFolderPath,
                    ParentId = "src",
                    Path = AppFolderPath,
                    Label = "Fixture.App",
                    Kind = WorkspaceTreeNodeKind.Folder,
                    HasChildren = true,
                    ChildCount = 2,
                    IsSelectable = true
                },
                new WorkspaceTreeNode
                {
                    Id = $"{AppFolderPath}/Fixture.App.csproj",
                    ParentId = AppFolderPath,
                    Path = $"{AppFolderPath}/Fixture.App.csproj",
                    Label = "Fixture.App",
                    Kind = WorkspaceTreeNodeKind.Project,
                    IsSelectable = true
                },
                new WorkspaceTreeNode
                {
                    Id = $"{AppFolderPath}/HttpEndpoint.cs",
                    ParentId = AppFolderPath,
                    Path = $"{AppFolderPath}/HttpEndpoint.cs",
                    Label = "HttpEndpoint.cs",
                    Kind = WorkspaceTreeNodeKind.File,
                    IsSelectable = true
                },
                new WorkspaceTreeNode
                {
                    Id = CoreFolderPath,
                    ParentId = "src",
                    Path = CoreFolderPath,
                    Label = "Fixture.Core",
                    Kind = WorkspaceTreeNodeKind.Folder,
                    HasChildren = true,
                    ChildCount = 2,
                    IsSelectable = true
                },
                new WorkspaceTreeNode
                {
                    Id = $"{CoreFolderPath}/Fixture.Core.csproj",
                    ParentId = CoreFolderPath,
                    Path = $"{CoreFolderPath}/Fixture.Core.csproj",
                    Label = "Fixture.Core",
                    Kind = WorkspaceTreeNodeKind.Project,
                    IsSelectable = true
                },
                new WorkspaceTreeNode
                {
                    Id = $"{CoreFolderPath}/MessageProvider.cs",
                    ParentId = CoreFolderPath,
                    Path = $"{CoreFolderPath}/MessageProvider.cs",
                    Label = "MessageProvider.cs",
                    Kind = WorkspaceTreeNodeKind.File,
                    IsSelectable = true
                });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }
}
