using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;

namespace SharpSense.IntegrationTests;

public sealed class UiCommandIntegrationTests
{
    private const string RepositoryRoot = "/repo";
    private const string AppFolderPath = "src/Fixture.App";
    private const string CoreFolderPath = "src/Fixture.Core";
    private const string RootTreePath = "%2F";

    [Fact]
    public async Task GraphPages_ReturnCompactCompleteResultsAndValidateRevisionAndCursor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await UiCommandTestDatabase.Create();
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("fixture", RepositoryRoot, []);
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var app = Cli.Program.CreateCommandApp(configureServices: services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton(catalog);
            database.ConfigureServices(services);
        }, enableFileLogging: false);
        using var shutdown = new CancellationTokenSource();
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        try
        {
            await WaitForServer(runTask, $"{baseUrl}/");
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var missingWorkspace = await client.GetAsync("/api/graph/nodes/page?directoryIds=1", ct);
            Assert.Equal(HttpStatusCode.BadRequest, missingWorkspace.StatusCode);
            using var missingConnectionsWorkspace = await client.GetAsync("/api/graph/nodes/200/connections", ct);
            Assert.Equal(HttpStatusCode.BadRequest, missingConnectionsWorkspace.StatusCode);
            client.DefaultRequestHeaders.Add("X-SharpSense-Workspace", selection.Definition.Id.ToString());

            using var first = JsonDocument.Parse(await client.GetStringAsync(
                "/api/graph/nodes/page?directoryIds=1&pageSize=1&includeTotal=true", ct));
            var revision = first.RootElement.GetProperty("revision").GetString();
            Assert.Equal(4, first.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(100, Assert.Single(first.RootElement.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt32());
            var cursor = first.RootElement.GetProperty("nextCursor").GetString();
            using var next = JsonDocument.Parse(await client.GetStringAsync(
                $"/api/graph/nodes/page?directoryIds=1&pageSize=5&cursor={Uri.EscapeDataString(cursor!)}", ct));
            Assert.Equal(3, next.RootElement.GetProperty("items").GetArrayLength());
            Assert.Equal(JsonValueKind.Null, next.RootElement.GetProperty("nextCursor").ValueKind);

            using var edges = JsonDocument.Parse(await client.GetStringAsync(
                $"/api/graph/edges/page?directoryIds=1&revision={revision}&includeTotal=true", ct));
            Assert.Equal(2, edges.RootElement.GetProperty("totalCount").GetInt32());
            Assert.All(edges.RootElement.GetProperty("items").EnumerateArray(), edge =>
            {
                Assert.Equal(JsonValueKind.Number, edge.GetProperty("source").ValueKind);
                Assert.Equal(JsonValueKind.Number, edge.GetProperty("target").ValueKind);
                Assert.False(edge.TryGetProperty("id", out _));
            });

            foreach (var query in new[] { "pageSize=0", "pageSize=5001", "cursor=invalid!", "directoryIds=-1" })
            {
                using var invalid = await client.GetAsync($"/api/graph/nodes/page?{query}", ct);
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            }
            using var stale = await client.GetAsync("/api/graph/edges/page?directoryIds=1&revision=old", ct);
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            using var connections = JsonDocument.Parse(await client.GetStringAsync(
                "/api/graph/nodes/200/connections?includeTotal=true", ct));
            Assert.Equal(200, connections.RootElement.GetProperty("node").GetProperty("id").GetInt32());
            Assert.Equal(1, connections.RootElement.GetProperty("totalCount").GetInt32());
            var connectedPeer = Assert.Single(connections.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(201, connectedPeer.GetProperty("node").GetProperty("id").GetInt32());
            var relationship = Assert.Single(connectedPeer.GetProperty("relationships").EnumerateArray());
            Assert.Equal("outgoing", relationship.GetProperty("direction").GetString());
            Assert.Equal("methodcall", relationship.GetProperty("type").GetString());
            foreach (var (path, expectedStatus) in new[]
            {
                ("/api/graph/nodes/999999/connections", HttpStatusCode.NotFound),
                ("/api/graph/nodes/0/connections", HttpStatusCode.BadRequest),
                ("/api/graph/nodes/200/connections?pageSize=501", HttpStatusCode.BadRequest),
                ("/api/graph/nodes/200/connections?cursor=invalid!", HttpStatusCode.BadRequest),
                ("/api/graph/nodes/200/connections?revision=old", HttpStatusCode.Conflict)
            })
            {
                using var response = await client.GetAsync(path, ct);
                Assert.Equal(expectedStatus, response.StatusCode);
            }

            using var compressedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/graph/nodes/page?directoryIds=1");
            compressedRequest.Headers.AcceptEncoding.ParseAdd("gzip");
            using var compressed = await client.SendAsync(compressedRequest, ct);
            Assert.Equal(HttpStatusCode.OK, compressed.StatusCode);
            Assert.Contains("gzip", compressed.Content.Headers.ContentEncoding);
            await using var compressedBody = await compressed.Content.ReadAsStreamAsync(ct);
            await using var decompressed = new System.IO.Compression.GZipStream(compressedBody,
                System.IO.Compression.CompressionMode.Decompress);
            using var compressedPage = await JsonDocument.ParseAsync(decompressed, cancellationToken: ct);
            Assert.Equal(4, compressedPage.RootElement.GetProperty("items").GetArrayLength());
        }
        finally
        {
            shutdown.Cancel();
            await runTask;
        }
    }

    [Fact]
    public async Task WhenUiCommandRuns_ThenTreeAndScopedGraphEndpointsReturnAnalyzedData()
    {
        await using var database = await UiCommandTestDatabase.Create();
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("fixture", RepositoryRoot, []);
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                services.AddSingleton(catalog);
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
            httpClient.DefaultRequestHeaders.Add("X-SharpSense-Workspace", selection.Definition.Id.ToString());

            using var graphNodesResponse = await httpClient.GetAsync($"{baseUrl}/api/graph/nodes/page", TestContext.Current.CancellationToken);
            using var graphEdgesResponse = await httpClient.GetAsync($"{baseUrl}/api/graph/edges/page", TestContext.Current.CancellationToken);
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
            var nodes = graphNodesDocument.RootElement.GetProperty("items");
            var edges = graphEdgesDocument.RootElement.GetProperty("items");
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
                $"{baseUrl}/api/graph/nodes/page?directoryIds={appDirectoryId}",
                TestContext.Current.CancellationToken);
            using var scopedGraphEdgesResponse = await httpClient.GetAsync(
                $"{baseUrl}/api/graph/edges/page?directoryIds={appDirectoryId}",
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, scopedGraphNodesResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, scopedGraphEdgesResponse.StatusCode);
            await using var scopedGraphNodesStream = await scopedGraphNodesResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            await using var scopedGraphEdgesStream = await scopedGraphEdgesResponse.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
            using var scopedGraphNodesDocument = await JsonDocument.ParseAsync(scopedGraphNodesStream, cancellationToken: TestContext.Current.CancellationToken);
            using var scopedGraphEdgesDocument = await JsonDocument.ParseAsync(scopedGraphEdgesStream, cancellationToken: TestContext.Current.CancellationToken);
            var scopedNodes = scopedGraphNodesDocument.RootElement.GetProperty("items");
            var scopedEdges = scopedGraphEdgesDocument.RootElement.GetProperty("items");

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
                    node.GetProperty("id").GetInt32() == 100 &&
                    node.GetProperty("scope").GetString() == "selected");
            Assert.Contains(
                scopedNodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetInt32() == 200 &&
                    node.GetProperty("label").GetString() == "Fixture.App.HttpEndpoint.Handle()" &&
                    node.GetProperty("codeNodeId").GetInt32() == 200 &&
                    node.GetProperty("type").GetString() == "method" &&
                    node.GetProperty("scope").GetString() == "selected");
            Assert.Contains(
                scopedNodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetInt32() == 101 &&
                    node.GetProperty("scope").GetString() == "external");
            Assert.Contains(
                scopedNodes.EnumerateArray(),
                node =>
                    node.GetProperty("id").GetInt32() == 201 &&
                    node.GetProperty("label").GetString() == "Fixture.Core.MessageProvider.GetMessage()" &&
                    node.GetProperty("type").GetString() == "method" &&
                    node.GetProperty("scope").GetString() == "external");
            Assert.Contains(
                scopedEdges.EnumerateArray(),
                edge =>
                    edge.GetProperty("source").GetInt32() == 100 &&
                    edge.GetProperty("target").GetInt32() == 101 &&
                    edge.GetProperty("type").GetString() == "projectreference" &&
                    edge.GetProperty("scope").GetString() == "boundary");
            Assert.Contains(
                scopedEdges.EnumerateArray(),
                edge =>
                    edge.GetProperty("source").GetInt32() == 200 &&
                    edge.GetProperty("target").GetInt32() == 201 &&
                    edge.GetProperty("type").GetString() == "methodcall" &&
                    edge.GetProperty("scope").GetString() == "boundary");

            foreach (var retired in new[] { "/api/graph/view", "/api/graph/nodes", "/api/graph/edges" })
            {
                using var retiredResponse = await httpClient.GetAsync(baseUrl + retired, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.NotFound, retiredResponse.StatusCode);
                Assert.Equal("application/problem+json", retiredResponse.Content.Headers.ContentType?.MediaType);
            }

            using var overview = await httpClient.GetAsync($"{baseUrl}/api/overview", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
            using var overviewJson = JsonDocument.Parse(await overview.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(overviewJson.RootElement.GetProperty("indexed").GetBoolean());
            Assert.Equal(2, overviewJson.RootElement.GetProperty("nodeCount").GetInt32());

            using var openApi = await httpClient.GetAsync($"{baseUrl}/openapi/v1.json", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
            using var schema = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.True(schema.RootElement.GetProperty("paths").TryGetProperty("/api/tools/search", out _));
            foreach (var retired in new[] { "/api/graph/view", "/api/graph/nodes", "/api/graph/edges" })
            {
                Assert.False(schema.RootElement.GetProperty("paths").TryGetProperty(retired, out _));
            }
            var connectionsHeader = schema.RootElement.GetProperty("paths").GetProperty("/api/graph/nodes/{nodeId}/connections")
                .GetProperty("get").GetProperty("parameters").EnumerateArray()
                .Single(parameter => parameter.GetProperty("name").GetString() == "X-SharpSense-Workspace");
            Assert.True(connectionsHeader.GetProperty("required").GetBoolean());
            Assert.Equal("uuid", connectionsHeader.GetProperty("schema").GetProperty("format").GetString());
            Assert.Contains("codeNodeId", schema.RootElement.ToString());
            Assert.Equal("integer", schema.RootElement.GetProperty("components").GetProperty("schemas")
                .GetProperty("WorkspaceOverview").GetProperty("properties").GetProperty("nodeCount").GetProperty("type").GetString());

            foreach (var tool in new[] { "context", "trace", "inheritors", "impact" })
            {
                using var toolResponse = await httpClient.PostAsJsonAsync($"{baseUrl}/api/tools/{tool}", new { nodeId = 200 }, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.OK, toolResponse.StatusCode);
                using var missing = await httpClient.PostAsJsonAsync($"{baseUrl}/api/tools/{tool}", new { nodeId = 999999 }, TestContext.Current.CancellationToken);
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
                Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
            }
            using var invalidSearch = await httpClient.PostAsJsonAsync($"{baseUrl}/api/tools/search", new { query = "", limit = 10 }, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalidSearch.StatusCode);
            using var unknownApi = await httpClient.GetAsync($"{baseUrl}/api/missing", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, unknownApi.StatusCode);

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

    [Fact]
    public async Task WhenTracingOwnedMembers_ThenExcludesStructuralEdgesAndMarksOnlyCodeParentsSelectable()
    {
        await using var database = await UiCommandTestDatabase.Create();
        await database.AddStructuralRelationships();
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("fixture", RepositoryRoot, []);
        var app = Cli.Program.CreateCommandApp(configureServices: services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton(catalog);
            database.ConfigureServices(services);
        }, enableFileLogging: false);
        using var shutdown = new CancellationTokenSource();
        var runTask = app.RunAsync(["ui", "--url", baseUrl, "--repo-root", RepositoryRoot], shutdown.Token);
        try
        {
            await WaitForServer(runTask, $"{baseUrl}/");
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.Add("X-SharpSense-Workspace", selection.Definition.Id.ToString());
            var ct = TestContext.Current.CancellationToken;
            using var trace = await client.PostAsJsonAsync($"{baseUrl}/api/tools/trace",
                new { nodeId = 200, direction = "callee", maxDepth = 3 }, ct);
            Assert.Equal(HttpStatusCode.OK, trace.StatusCode);
            using var traceJson = JsonDocument.Parse(await trace.Content.ReadAsStringAsync(ct));
            var dependencies = traceJson.RootElement.GetProperty("dependencies").EnumerateArray().ToArray();
            Assert.Equal(2, dependencies.Length);
            Assert.DoesNotContain(dependencies, edge =>
                string.Equals(edge.GetProperty("edgeType").GetString(), "ParentOf", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(traceJson.RootElement.GetProperty("nodes").EnumerateArray(),
                node => node.GetProperty("id").GetInt32() == 202);

            using var member = await client.PostAsJsonAsync($"{baseUrl}/api/tools/context", new { nodeId = 200 }, ct);
            using var memberJson = JsonDocument.Parse(await member.Content.ReadAsStringAsync(ct));
            var codeParent = Assert.Single(memberJson.RootElement.GetProperty("parents").EnumerateArray());
            Assert.Equal(202, codeParent.GetProperty("codeNodeId").GetInt32());

            using var owner = await client.PostAsJsonAsync($"{baseUrl}/api/tools/context", new { nodeId = 202 }, ct);
            using var ownerJson = JsonDocument.Parse(await owner.Content.ReadAsStringAsync(ct));
            var projectParent = Assert.Single(ownerJson.RootElement.GetProperty("parents").EnumerateArray());
            Assert.Equal(100, projectParent.GetProperty("id").GetInt32());
            Assert.True(!projectParent.TryGetProperty("codeNodeId", out var codeNodeId) || codeNodeId.ValueKind == JsonValueKind.Null);
        }
        finally
        {
            shutdown.Cancel();
            await runTask;
        }
    }

    [Fact]
    public async Task GlobalUiStartsEmptyAndKeepsConcurrentWorkspaceRequestsAndMemoriesIsolated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var firstDatabase = await UiCommandTestDatabase.Create();
        await using var secondDatabase = await UiCommandTestDatabase.Create();
        await secondDatabase.AddStructuralRelationships();
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
            ["/repo/backend/Orders.csproj"] = new("<Project />"),
            ["/repo/frontend/tsconfig.json"] = new("{}"),
            ["/repo/docs/design.md"] = new("# Architecture"),
            ["/repo/node_modules/ignored/tsconfig.json"] = new("{}"),
            ["/repo/.git/ignored.csproj"] = new("<Project />")
        }, RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var firstId = Guid.Empty;
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var app = Cli.Program.CreateCommandApp(configureServices: services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton(catalog);
            services.RemoveAll<IDbContextFactory<SharpSenseDbContext>>();
            services.AddScoped(provider => provider.GetRequiredService<IRepositoryWorkspace>().WorkspaceId == firstId
                ? firstDatabase.GetFactory()
                : secondDatabase.GetFactory());
        }, enableFileLogging: false);
        using var shutdown = new CancellationTokenSource();
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        try
        {
            await WaitForServer(runTask, $"{baseUrl}/");
            using var globalClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var emptyCatalog = JsonDocument.Parse(await globalClient.GetStringAsync("/api/workspaces", ct));
            Assert.Empty(emptyCatalog.RootElement.GetProperty("workspaces").EnumerateArray());
            Assert.False(fileSystem.Directory.Exists(catalog.HomeDirectory));

            globalClient.DefaultRequestHeaders.Add("Origin", baseUrl);
            using var discovery = await globalClient.PostAsJsonAsync("/api/workspaces/discover", new { repositoryRoot = RepositoryRoot }, ct);
            Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
            using var candidates = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync(ct));
            Assert.Equal(new[] { "backend/Orders.csproj", "docs/*.md", "frontend/tsconfig.json" },
                candidates.RootElement.GetProperty("sources").EnumerateArray()
                    .Select(static source => source.GetProperty("path").GetString()).OrderBy(static path => path).ToArray());
            Assert.False(fileSystem.Directory.Exists(catalog.HomeDirectory));

            firstId = await CreateWorkspace("first");
            var secondId = await CreateWorkspace("second");
            using var firstClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var secondClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
            firstClient.DefaultRequestHeaders.Add("X-SharpSense-Workspace", firstId.ToString());
            secondClient.DefaultRequestHeaders.Add("X-SharpSense-Workspace", secondId.ToString());

            using var missingSelection = await globalClient.GetAsync("/api/overview", ct);
            Assert.Equal(HttpStatusCode.BadRequest, missingSelection.StatusCode);
            using var unknownRequest = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
            unknownRequest.Headers.Add("X-SharpSense-Workspace", Guid.NewGuid().ToString());
            using var unknownSelection = await globalClient.SendAsync(unknownRequest, ct);
            Assert.Equal(HttpStatusCode.NotFound, unknownSelection.StatusCode);

            var overviews = await Task.WhenAll(
                firstClient.GetStringAsync("/api/overview", ct),
                secondClient.GetStringAsync("/api/overview", ct));
            using var firstOverview = JsonDocument.Parse(overviews[0]);
            using var secondOverview = JsonDocument.Parse(overviews[1]);
            Assert.Equal(firstId, firstOverview.RootElement.GetProperty("workspaceId").GetGuid());
            Assert.Equal(secondId, secondOverview.RootElement.GetProperty("workspaceId").GetGuid());
            Assert.Equal(2, firstOverview.RootElement.GetProperty("nodeCount").GetInt32());
            Assert.Equal(3, secondOverview.RootElement.GetProperty("nodeCount").GetInt32());

            using var firstConnections = JsonDocument.Parse(await firstClient.GetStringAsync("/api/graph/nodes/200/connections?includeTotal=true", ct));
            using var secondConnections = JsonDocument.Parse(await secondClient.GetStringAsync("/api/graph/nodes/200/connections?includeTotal=true", ct));
            Assert.Equal(1, firstConnections.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(2, secondConnections.RootElement.GetProperty("totalCount").GetInt32());

            using var attached = await firstClient.PostAsJsonAsync("/api/memory/node/200",
                new { content = "Belongs only to first workspace", tags = new[] { "scope" }, intent = "Invariant" }, ct);
            Assert.Equal(HttpStatusCode.OK, attached.StatusCode);
            using var firstMemories = JsonDocument.Parse(await firstClient.GetStringAsync("/api/memory/node/200", ct));
            using var secondMemories = JsonDocument.Parse(await secondClient.GetStringAsync("/api/memory/node/200", ct));
            Assert.Single(firstMemories.RootElement.EnumerateArray());
            Assert.Empty(secondMemories.RootElement.EnumerateArray());

            using var renamed = await globalClient.PutAsJsonAsync($"/api/workspaces/{firstId}",
                new { name = "renamed-first", sources = Array.Empty<object>() }, ct);
            Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
            Assert.Equal(firstId, catalog.Resolve("renamed-first", RepositoryRoot).Definition.Id);

            using var schema = JsonDocument.Parse(await globalClient.GetStringAsync("/openapi/v1.json", ct));
            foreach (var path in schema.RootElement.GetProperty("paths").EnumerateObject())
            {
                var scoped = !path.Name.StartsWith("/api/workspaces", StringComparison.Ordinal) && path.Name != "/api/tools";
                foreach (var operation in path.Value.EnumerateObject().Where(static property =>
                             property.Name is "get" or "post" or "put" or "patch" or "delete"))
                {
                    var headers = operation.Value.TryGetProperty("parameters", out var parameters)
                        ? parameters.EnumerateArray().Where(static parameter => parameter.TryGetProperty("name", out var name) &&
                            name.GetString() == "X-SharpSense-Workspace").ToArray()
                        : [];
                    if (!scoped)
                    {
                        Assert.Empty(headers);
                        continue;
                    }

                    var header = Assert.Single(headers);
                    Assert.Equal("header", header.GetProperty("in").GetString());
                    Assert.True(header.GetProperty("required").GetBoolean());
                    Assert.Equal("string", header.GetProperty("schema").GetProperty("type").GetString());
                    Assert.Equal("uuid", header.GetProperty("schema").GetProperty("format").GetString());
                }
            }

            async Task<Guid> CreateWorkspace(string name)
            {
                using var response = await globalClient.PostAsJsonAsync("/api/workspaces",
                    new { name, repositoryRoot = RepositoryRoot, sources = Array.Empty<object>() }, ct);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                return json.RootElement.GetProperty("id").GetGuid();
            }
        }
        finally
        {
            shutdown.Cancel();
            await runTask;
        }
    }

    [Fact]
    public async Task GlobalUiRejectsCrossOriginAndUnexpectedHostBeforeCatalogWrites()
    {
        var ct = TestContext.Current.CancellationToken;
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var app = Cli.Program.CreateCommandApp(configureServices: services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton(catalog);
        }, enableFileLogging: false);
        using var shutdown = new CancellationTokenSource();
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        try
        {
            await WaitForServer(runTask, $"{baseUrl}/");
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            foreach (var (header, value) in new[] { ("Origin", "https://untrusted.example"), ("Host", "untrusted.example"), ("Sec-Fetch-Site", "cross-site") })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
                {
                    Content = JsonContent.Create(new { name = "blocked", repositoryRoot = RepositoryRoot, sources = Array.Empty<object>() })
                };
                request.Headers.TryAddWithoutValidation(header, value);
                using var response = await client.SendAsync(request, ct);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }

            Assert.Empty(catalog.List());
            Assert.False(fileSystem.Directory.Exists(catalog.HomeDirectory));
        }
        finally
        {
            shutdown.Cancel();
            await runTask;
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

    [Fact]
    public async Task WhenMemoryEndpointsAreCalled_ThenTheyAttachListFetchAndDelete()
    {
        await using var database = await UiCommandTestDatabase.Create();
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("fixture", RepositoryRoot, []);
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                services.AddSingleton(catalog);
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

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            httpClient.DefaultRequestHeaders.Add("X-SharpSense-Workspace", selection.Definition.Id.ToString());

            const int nodeId = 200;
            var content = "Always greet politely: invariant the team expects.";

            using (var addResponse = await httpClient.PostAsync(
                $"{baseUrl}/api/memory/node/{nodeId}",
                new StringContent(
                    $"{{\"content\":\"{content}\",\"tags\":[\"convention\"],\"intent\":\"Invariant\"}}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
            }

            var listJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/node/{nodeId}",
                TestContext.Current.CancellationToken);
            using var listDocument = JsonDocument.Parse(listJson);
            var memories = listDocument.RootElement.EnumerateArray().ToArray();
            Assert.Single(memories);
            var memoryId = memories[0].GetProperty("id").GetString()!;
            Assert.Equal("Invariant", memories[0].GetProperty("intent").GetString());

            var singleJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/{memoryId}",
                TestContext.Current.CancellationToken);
            using var singleDocument = JsonDocument.Parse(singleJson);
            Assert.Equal(content, singleDocument.RootElement.GetProperty("content").GetString());

            var batchJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory?ids={memoryId}",
                TestContext.Current.CancellationToken);
            using var batchDocument = JsonDocument.Parse(batchJson);
            Assert.Equal(1, batchDocument.RootElement.GetArrayLength());

            using var invalidIntent = await httpClient.GetAsync($"{baseUrl}/api/memory/node/{nodeId}?intents=Unknown", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.BadRequest, invalidIntent.StatusCode);
            Assert.Equal("application/problem+json", invalidIntent.Content.Headers.ContentType?.MediaType);

            var filteredJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/node/{nodeId}?intents=Invariant",
                TestContext.Current.CancellationToken);
            using var filteredDocument = JsonDocument.Parse(filteredJson);
            Assert.Equal(1, filteredDocument.RootElement.GetArrayLength());

            var noneJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/node/{nodeId}?intents=Warning",
                TestContext.Current.CancellationToken);
            using var noneDocument = JsonDocument.Parse(noneJson);
            Assert.Equal(0, noneDocument.RootElement.GetArrayLength());

            using (var deleteResponse = await httpClient.DeleteAsync(
                $"{baseUrl}/api/memory/{memoryId}",
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
            }

            var afterJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/node/{nodeId}",
                TestContext.Current.CancellationToken);
            using var afterDocument = JsonDocument.Parse(afterJson);
            Assert.Equal(0, afterDocument.RootElement.GetArrayLength());
        }
        finally
        {
            shutdown.Cancel();
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
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

        public IDbContextFactory<SharpSenseDbContext> GetFactory() => contextFactory.CreateDbContextFactory<SharpSenseDbContext>();

        public async ValueTask DisposeAsync()
        {
            await contextFactory.DisposeAsync();
        }

        public async Task AddStructuralRelationships()
        {
            var ct = TestContext.Current.CancellationToken;
            await using var context = await contextFactory.GetContext<SharpSenseDbContext>(ct);
            context.GraphNodes.Add(new GraphNodeRecord
            {
                Id = 202, CanonicalId = "code:Fixture.App:Fixture.App.HttpEndpoint", Kind = GraphNodeKind.Code
            });
            context.CodeNodes.Add(new CodeNodeRecord
            {
                Id = 202, ProjectNodeId = 100, DocumentId = 12,
                FullyQualifiedName = "Fixture.App.HttpEndpoint", DisplayName = "HttpEndpoint",
                NodeType = NodeType.Class, StartLine = 1, EndLine = 15, Summary = "Owns endpoint handler."
            });
            context.DependencyEdges.AddRange(
                new DependencyEdgeRecord { CallerNodeId = 100, CalleeNodeId = 202, EdgeType = EdgeType.ParentOf },
                new DependencyEdgeRecord { CallerNodeId = 202, CalleeNodeId = 200, EdgeType = EdgeType.ParentOf },
                new DependencyEdgeRecord { CallerNodeId = 200, CalleeNodeId = 202, EdgeType = EdgeType.Instantiates });
            await context.SaveChangesAsync(ct);
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
