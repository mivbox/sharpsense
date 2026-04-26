using System.Net;
using System.Net.Sockets;
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
    [Fact]
    public async Task WhenUiCommandRuns_ThenEndpointsReturnSuccessAndGraphIsServedFromInMemoryContext()
    {
        await using var database = await UiCommandTestDatabase.Create();
        var repositoryRoot = CreateRepositoryRoot();
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var logFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".SharpSense",
            "logs",
            "ui.log");
        var app = Cli.Program.CreateCommandApp(configureServices: database.ConfigureServices);
        using var shutdown = new CancellationTokenSource();
        var runTask = app.RunAsync(
            ["ui", "--url", baseUrl, "--repo-root", repositoryRoot],
            shutdown.Token);

        try
        {
            await WaitForServer(runTask, $"{baseUrl}/");

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            using var graphResponse = await httpClient.GetAsync($"{baseUrl}/api/graph", TestContext.Current.CancellationToken);
            using var rootResponse = await httpClient.GetAsync($"{baseUrl}/", TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, graphResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);

            await using var graphStream = await graphResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            using var jsonDocument = await JsonDocument.ParseAsync(graphStream, cancellationToken: TestContext.Current.CancellationToken);
            var nodes = jsonDocument.RootElement.GetProperty("nodes");
            var edges = jsonDocument.RootElement.GetProperty("edges");

            Assert.Contains(
                nodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetString() == UiCommandTestDatabase.CallerCanonicalId &&
                    node.GetProperty("label").GetString() == "Fixture.App.HttpEndpoint.Handle()" &&
                    node.GetProperty("type").GetString() == "method");
            Assert.Contains(
                nodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetString() == UiCommandTestDatabase.CalleeCanonicalId &&
                    node.GetProperty("label").GetString() == "Fixture.App.MessageProvider.GetMessage()" &&
                    node.GetProperty("type").GetString() == "method");
            Assert.Contains(
                edges.EnumerateArray(),
                edge =>
                    edge.GetProperty("source").GetString() == UiCommandTestDatabase.CallerCanonicalId &&
                    edge.GetProperty("target").GetString() == UiCommandTestDatabase.CalleeCanonicalId &&
                    edge.GetProperty("type").GetString() == "methodcall");

            shutdown.Cancel();
            Assert.Equal(0, await runTask);

            var logContents = await WaitForLogContents(logFilePath, baseUrl);

            Assert.Contains($"Configuring UI application for {baseUrl}", logContents, StringComparison.Ordinal);
            Assert.Contains($"Request starting HTTP/1.1 GET {baseUrl}/api/graph", logContents, StringComparison.Ordinal);
            Assert.Contains("HTTP GET / responded 200", logContents, StringComparison.Ordinal);
        }
        finally
        {
            shutdown.Cancel();

            if (!runTask.IsCompleted)
            {
                await runTask;
            }

            DeleteDirectoryIfExists(repositoryRoot);
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

    private static async Task<string> WaitForLogContents(string logFilePath, string baseUrl)
    {
        var startedAtUtc = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAtUtc < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(logFilePath))
            {
                var logContents = await File.ReadAllTextAsync(logFilePath, TestContext.Current.CancellationToken);
                if (logContents.Contains(baseUrl, StringComparison.Ordinal))
                {
                    return logContents;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
        }

        throw new FileNotFoundException($"The UI log file '{logFilePath}' did not contain the expected URL '{baseUrl}'.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateRepositoryRoot()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), $"sharp-sense-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".git"));
        return repositoryRoot;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class UiCommandTestDatabase(InMemoryContextFactory contextFactory) : IAsyncDisposable
    {
        public const string ProjectId = "project-app";
        public const string CallerCanonicalId = "code:project-app:Fixture.App.HttpEndpoint.Handle()";
        public const string CalleeCanonicalId = "code:project-app:Fixture.App.MessageProvider.GetMessage()";

        public static async Task<UiCommandTestDatabase> Create()
        {
            var contextFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(UseMigrations: true));
            var database = new UiCommandTestDatabase(contextFactory);
            await database.Initialize();
            return database;
        }

        public Action<IServiceCollection> ConfigureServices => services =>
            contextFactory.ConfigureServices<SharpSenseDbContext>(services);

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
                Id = ProjectId,
                Name = "Fixture.App",
                RelativeFilePath = "src/Fixture.App/Fixture.App.csproj",
                ContentHash = "project-hash"
            });

            dbContext.CodeNodes.AddRange(
                new CodeNode
                {
                    Id = 1,
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
                    Id = 2,
                    CanonicalId = CalleeCanonicalId,
                    ProjectId = ProjectId,
                    FullyQualifiedName = "Fixture.App.MessageProvider.GetMessage()",
                    DisplayName = "MessageProvider.GetMessage()",
                    NodeType = NodeType.Method,
                    RelativeFilePath = "src/Fixture.App/MessageProvider.cs",
                    StartLine = 7,
                    EndLine = 11,
                    Summary = "Gets a message."
                });

            dbContext.DependencyEdges.Add(new DependencyEdge
            {
                CallerId = CallerCanonicalId,
                CalleeId = CalleeCanonicalId,
                EdgeType = EdgeType.MethodCall
            });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }
}
