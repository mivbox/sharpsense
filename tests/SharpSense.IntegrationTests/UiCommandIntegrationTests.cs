using System.Net;
using System.Net.Sockets;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
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

            using var graphNodesResponse = await httpClient.GetAsync($"{baseUrl}/api/graph/nodes", TestContext.Current.CancellationToken);
            using var graphEdgesResponse = await httpClient.GetAsync($"{baseUrl}/api/graph/edges", TestContext.Current.CancellationToken);
            using var rootTreeResponse = await httpClient.GetAsync($"{baseUrl}/api/tree?path={RootTreePath}", TestContext.Current.CancellationToken);
            using var srcTreeResponse = await httpClient.GetAsync($"{baseUrl}/api/tree?path=src", TestContext.Current.CancellationToken);
            using var rootResponse = await httpClient.GetAsync($"{baseUrl}/", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, graphNodesResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, graphEdgesResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, rootTreeResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, srcTreeResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);

            await using var graphNodesStream = await graphNodesResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            await using var graphEdgesStream = await graphEdgesResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            await using var rootTreeStream = await rootTreeResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            await using var srcTreeStream = await srcTreeResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            using var graphNodesDocument = await JsonDocument.ParseAsync(graphNodesStream, cancellationToken: TestContext.Current.CancellationToken);
            using var graphEdgesDocument = await JsonDocument.ParseAsync(graphEdgesStream, cancellationToken: TestContext.Current.CancellationToken);
            using var rootTreeDocument = await JsonDocument.ParseAsync(rootTreeStream, cancellationToken: TestContext.Current.CancellationToken);
            using var srcTreeDocument = await JsonDocument.ParseAsync(srcTreeStream, cancellationToken: TestContext.Current.CancellationToken);
            var nodes = graphNodesDocument.RootElement;
            var edges = graphEdgesDocument.RootElement;
            var rootTreeNodes = rootTreeDocument.RootElement.GetProperty("nodes");
            var srcTreeNodes = srcTreeDocument.RootElement.GetProperty("nodes");
            var appDirectoryId = srcTreeNodes.EnumerateArray()
                .First(
                    node =>
                        node.GetProperty("path").GetString() == AppFolderPath &&
                    node.GetProperty("kind").GetString() == "folder")
                .GetProperty("id")
                .GetInt32();
            using var scopedGraphNodesResponse = await httpClient.GetAsync(
                $"{baseUrl}/api/graph/nodes?directoryIds={appDirectoryId}",
                TestContext.Current.CancellationToken);
            using var scopedGraphEdgesResponse = await httpClient.GetAsync(
                $"{baseUrl}/api/graph/edges?directoryIds={appDirectoryId}",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, scopedGraphNodesResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, scopedGraphEdgesResponse.StatusCode);
            await using var scopedGraphNodesStream = await scopedGraphNodesResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            await using var scopedGraphEdgesStream = await scopedGraphEdgesResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            using var scopedGraphNodesDocument = await JsonDocument.ParseAsync(scopedGraphNodesStream, cancellationToken: TestContext.Current.CancellationToken);
            using var scopedGraphEdgesDocument = await JsonDocument.ParseAsync(scopedGraphEdgesStream, cancellationToken: TestContext.Current.CancellationToken);
            var scopedNodes = scopedGraphNodesDocument.RootElement;
            var scopedEdges = scopedGraphEdgesDocument.RootElement;

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
                    Path = AppFolderPath,
                    Name = "Fixture.App"
                },
                new DirectoryRecord
                {
                    Id = 4,
                    ParentId = 2,
                    Path = CoreFolderPath,
                    Name = "Fixture.Core"
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
                    Depth = 2
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 2,
                    DescendantDirectoryId = 3,
                    Depth = 1
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 2,
                    DescendantDirectoryId = 4,
                    Depth = 1
                });
            dbContext.Documents.AddRange(
                new DocumentRecord
                {
                    Id = 10,
                    DirectoryId = 3,
                    FileName = "Fixture.App.csproj",
                    Extension = ".csproj",
                    RelativePath = $"{AppFolderPath}/Fixture.App.csproj",
                    Kind = DocumentKind.ProjectFile
                },
                new DocumentRecord
                {
                    Id = 11,
                    DirectoryId = 4,
                    FileName = "Fixture.Core.csproj",
                    Extension = ".csproj",
                    RelativePath = $"{CoreFolderPath}/Fixture.Core.csproj",
                    Kind = DocumentKind.ProjectFile
                },
                new DocumentRecord
                {
                    Id = 12,
                    DirectoryId = 3,
                    FileName = "HttpEndpoint.cs",
                    Extension = ".cs",
                    RelativePath = $"{AppFolderPath}/HttpEndpoint.cs",
                    Kind = DocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 13,
                    DirectoryId = 4,
                    FileName = "MessageProvider.cs",
                    Extension = ".cs",
                    RelativePath = $"{CoreFolderPath}/MessageProvider.cs",
                    Kind = DocumentKind.Source
                });
            dbContext.GraphNodes.AddRange(
                new GraphNodeRecord
                {
                    Id = 100,
                    CanonicalId = AppProjectId,
                    Kind = GraphNodeKind.Project
                },
                new GraphNodeRecord
                {
                    Id = 101,
                    CanonicalId = CoreProjectId,
                    Kind = GraphNodeKind.Project
                },
                new GraphNodeRecord
                {
                    Id = 200,
                    CanonicalId = CallerCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = 201,
                    CanonicalId = CalleeCanonicalId,
                    Kind = GraphNodeKind.Code
                });
            dbContext.ProjectNodes.Add(new ProjectNodeRecord
            {
                Id = 100,
                Name = "Fixture.App",
                ProjectDocumentId = 10,
                ContentHash = "project-hash"
            });
            dbContext.ProjectNodes.Add(new ProjectNodeRecord
            {
                Id = 101,
                Name = "Fixture.Core",
                ProjectDocumentId = 11,
                ContentHash = "project-core-hash"
            });

            dbContext.CodeNodes.AddRange(
                new CodeNodeRecord
                {
                    Id = 200,
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
                    Id = 201,
                    ProjectNodeId = 101,
                    DocumentId = 13,
                    FullyQualifiedName = "Fixture.Core.MessageProvider.GetMessage()",
                    DisplayName = "MessageProvider.GetMessage()",
                    NodeType = NodeType.Method,
                    StartLine = 7,
                    EndLine = 11,
                    Summary = "Gets a message."
                });

            dbContext.DependencyEdges.Add(new DependencyEdgeRecord
            {
                CallerNodeId = 100,
                CalleeNodeId = 101,
                EdgeType = EdgeType.ProjectReference
            });
            dbContext.DependencyEdges.Add(new DependencyEdgeRecord
            {
                CallerNodeId = 200,
                CalleeNodeId = 201,
                EdgeType = EdgeType.MethodCall
            });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }
}
