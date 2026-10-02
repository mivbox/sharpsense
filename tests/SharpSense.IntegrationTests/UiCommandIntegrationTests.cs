using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace SharpSense.IntegrationTests;

public sealed class UiCommandIntegrationTests
{
    private const string RepositoryRoot = "/repo";
    private const string AppFolderPath = "src/Fixture.App";
    private const string CoreFolderPath = "src/Fixture.Core";
    private const string RootTreePath = "%2F";

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public async Task WhenDiagnosticRoutesHaveOptionalTrailingSlash_ThenKeepWorkspaceAndDatabasePolicies(string suffix)
    {
        var ct = TestContext.Current.CancellationToken;
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/Guide.md"] = new("# Guide")
        });
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var workspace = catalog.Create("fixture", "/repo", []);
        var initializer = new Moq.Mock<IWorkspaceDatabaseInitializer>(Moq.MockBehavior.Strict);
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var app = Cli.Program.CreateCommandApp(configureServices: services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton<IWorkspaceCatalog>(catalog);
            services.AddSingleton(initializer.Object);
        }, enableFileLogging: false);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        try
        {
            await WaitForServer(runTask, baseUrl, ct);
            using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
            using var tools = await client.GetAsync($"/api/tools{suffix}", ct);
            tools.StatusCode.Should().Be(HttpStatusCode.OK);
            using var noWorkspace = await client.GetAsync($"/api/tools/graph-stats{suffix}", ct);
            noWorkspace.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            client.DefaultRequestHeaders.Add("X-SharpSense-Workspace", workspace.Definition.Id.ToString());

            using var response = await client.GetAsync($"/api/tools/graph-stats{suffix}", ct);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var stats = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            stats.RootElement.GetProperty("databaseState").GetString().Should().Be("missing");
            fileSystem.File.Exists(workspace.Workspace.DatabasePath).Should().BeFalse();
            initializer.VerifyNoOtherCalls();
        }
        finally
        {
            await shutdown.CancelAsync();
            // Cleanup must finish even when the test is cancelled.
            await runTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenGraphPages_ThenReturnCompactCompleteResultsAndValidateRevisionAndCursor()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await UiCommandTestDatabase.Create(ct);
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("fixture", RepositoryRoot, []);
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                services.AddSingleton<IWorkspaceCatalog>(catalog);
                database.ConfigureServices(services);
            },
            enableFileLogging: false);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        try
        {
            await WaitForServer(
                runTask,
                $"{baseUrl}/",
                ct);
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };
            using var missingWorkspace = await client.GetAsync("/api/graph/nodes/page?directoryIds=1", ct);
            missingWorkspace.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var missingConnectionsWorkspace = await client.GetAsync("/api/graph/nodes/200/connections", ct);
            missingConnectionsWorkspace.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            client.DefaultRequestHeaders.Add("X-SharpSense-Workspace", selection.Definition.Id.ToString());

            using var first = JsonDocument.Parse(await client.GetStringAsync(
                "/api/graph/nodes/page?directoryIds=1&pageSize=1&includeTotal=true",
                ct));
            var revision = first.RootElement.GetProperty("revision")
                .GetString();
            first.RootElement.GetProperty("totalCount")
                .GetInt32().Should().Be(4);
            first.RootElement.GetProperty("items")
                .EnumerateArray().Should().ContainSingle().Which.GetProperty("id")
                .GetInt32().Should().Be(100);
            var cursor = first.RootElement.GetProperty("nextCursor")
                .GetString();
            using var next = JsonDocument.Parse(await client.GetStringAsync(
                $"/api/graph/nodes/page?directoryIds=1&pageSize=5&cursor={Uri.EscapeDataString(cursor!)}",
                ct));
            next.RootElement.GetProperty("items")
                .EnumerateArray()
                .Select(node => node.GetProperty("id").GetInt32())
                .Should()
                .Equal(101, 200, 201);
            next.RootElement.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);

            using var edges = JsonDocument.Parse(await client.GetStringAsync(
                $"/api/graph/edges/page?directoryIds=1&revision={revision}&includeTotal=true",
                ct));
            edges.RootElement.GetProperty("totalCount")
                .GetInt32().Should().Be(2);
            edges.RootElement.GetProperty("items")
                .EnumerateArray()
                .Select(edge => (
                    edge.GetProperty("source").GetInt32(),
                    edge.GetProperty("target").GetInt32(),
                    edge.GetProperty("type").GetString()))
                .Should()
                .BeEquivalentTo(new[] { (100, 101, "projectreference"), (200, 201, "methodcall") });
            edges.RootElement.GetProperty("items")
                .EnumerateArray().Should().AllSatisfy(edge =>
                {
                    edge.GetProperty("source").ValueKind.Should().Be(JsonValueKind.Number);
                    edge.GetProperty("target").ValueKind.Should().Be(JsonValueKind.Number);
                    edge.TryGetProperty("id", out _).Should().BeFalse();
                });

            foreach (var query in new[] { "pageSize=0", "pageSize=5001", "cursor=invalid!", "directoryIds=-1" })
            {
                using var invalid = await client.GetAsync(
                    $"/api/graph/nodes/page?{query}",
                    ct);
                invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }
            using var stale = await client.GetAsync("/api/graph/edges/page?directoryIds=1&revision=old", ct);
            stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

            using var connections = JsonDocument.Parse(await client.GetStringAsync(
                "/api/graph/nodes/200/connections?includeTotal=true",
                ct));
            connections.RootElement.GetProperty("node")
                .GetProperty("id")
                .GetInt32().Should().Be(200);
            connections.RootElement.GetProperty("totalCount")
                .GetInt32().Should().Be(1);
            var connectedPeer = connections.RootElement.GetProperty("items")
                .EnumerateArray().Should().ContainSingle().Which;
            connectedPeer.GetProperty("node")
                .GetProperty("id")
                .GetInt32().Should().Be(201);
            var relationship = connectedPeer.GetProperty("relationships")
                .EnumerateArray().Should().ContainSingle().Which;
            relationship.GetProperty("direction")
                .GetString().Should().Be("outgoing");
            relationship.GetProperty("type")
                .GetString().Should().Be("methodcall");
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
                response.StatusCode.Should().Be(expectedStatus);
            }

            using var compressedRequest = new HttpRequestMessage(HttpMethod.Get, "/api/graph/nodes/page?directoryIds=1");
            compressedRequest.Headers.AcceptEncoding.ParseAdd("gzip");
            using var compressed = await client.SendAsync(compressedRequest, ct);
            compressed.StatusCode.Should().Be(HttpStatusCode.OK);
            compressed.Content.Headers.ContentEncoding.Should().Contain("gzip");
            await using var compressedBody = await compressed.Content.ReadAsStreamAsync(ct);
            await using var decompressed = new System.IO.Compression.GZipStream(
                compressedBody,
                System.IO.Compression.CompressionMode.Decompress);
            using var compressedPage = await JsonDocument.ParseAsync(decompressed, cancellationToken: ct);
            compressedPage.RootElement.GetProperty("items")
                .GetArrayLength().Should().Be(4);
        }
        finally
        {
            shutdown.Cancel();
            // Cleanup must finish even when the test is cancelled.
            await runTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenWorkspaceTreeIsScoped_ThenGraphIncludesSelectedAndBoundaryNodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await UiHost.Start(ct);
        var httpClient = host.Client;
        var baseUrl = host.BaseUrl;

        using var graphNodesResponse = await httpClient.GetAsync(
            $"{baseUrl}/api/graph/nodes/page",
            ct);
        using var graphEdgesResponse = await httpClient.GetAsync(
            $"{baseUrl}/api/graph/edges/page",
            ct);
        using var rootTreeResponse = await httpClient.GetAsync(
            $"{baseUrl}/api/tree?path={RootTreePath}",
            ct);
        using var srcTreeResponse = await httpClient.GetAsync(
            $"{baseUrl}/api/tree?path=src",
            ct);
        using var rootResponse = await httpClient.GetAsync(
            $"{baseUrl}/",
            ct);
        graphNodesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        graphEdgesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        rootTreeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        srcTreeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        rootResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var graphNodesStream = await graphNodesResponse.Content.ReadAsStreamAsync(ct);
        await using var graphEdgesStream = await graphEdgesResponse.Content.ReadAsStreamAsync(ct);
        await using var rootTreeStream = await rootTreeResponse.Content.ReadAsStreamAsync(ct);
        await using var srcTreeStream = await srcTreeResponse.Content.ReadAsStreamAsync(ct);
        using var graphNodesDocument = await JsonDocument.ParseAsync(graphNodesStream, cancellationToken: ct);
        using var graphEdgesDocument = await JsonDocument.ParseAsync(graphEdgesStream, cancellationToken: ct);
        using var rootTreeDocument = await JsonDocument.ParseAsync(rootTreeStream, cancellationToken: ct);
        using var srcTreeDocument = await JsonDocument.ParseAsync(srcTreeStream, cancellationToken: ct);
        var nodes = graphNodesDocument.RootElement.GetProperty("items");
        var edges = graphEdgesDocument.RootElement.GetProperty("items");
        var rootTreeNodes = rootTreeDocument.RootElement.GetProperty("nodes");
        var srcTreeNodes = srcTreeDocument.RootElement.GetProperty("nodes");
        var appDirectoryId = srcTreeNodes.EnumerateArray()
            .First(
                node =>
                    node.GetProperty("path")
                        .GetString() == AppFolderPath &&
                node.GetProperty("kind")
                    .GetString() == "folder")
            .GetProperty("id")
            .GetInt32();
        using var scopedGraphNodesResponse = await httpClient.GetAsync(
            $"{baseUrl}/api/graph/nodes/page?directoryIds={appDirectoryId}",
            ct);
        using var scopedGraphEdgesResponse = await httpClient.GetAsync(
            $"{baseUrl}/api/graph/edges/page?directoryIds={appDirectoryId}",
            ct);
        scopedGraphNodesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        scopedGraphEdgesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scopedGraphNodesStream = await scopedGraphNodesResponse.Content.ReadAsStreamAsync(ct);
        await using var scopedGraphEdgesStream = await scopedGraphEdgesResponse.Content.ReadAsStreamAsync(ct);
        using var scopedGraphNodesDocument = await JsonDocument.ParseAsync(
            scopedGraphNodesStream,
            cancellationToken: ct);
        using var scopedGraphEdgesDocument = await JsonDocument.ParseAsync(
            scopedGraphEdgesStream,
            cancellationToken: ct);
        var scopedNodes = scopedGraphNodesDocument.RootElement.GetProperty("items");
        var scopedEdges = scopedGraphEdgesDocument.RootElement.GetProperty("items");

        nodes.EnumerateArray().Should().BeEmpty();
        edges.EnumerateArray().Should().BeEmpty();
        rootTreeNodes.EnumerateArray().Should().Contain(node =>
                node.GetProperty("path")
                    .GetString() == "src" &&
                node.GetProperty("kind")
                    .GetString() == "folder");
        srcTreeNodes.EnumerateArray().Should().Contain(node =>
                node.GetProperty("path")
                    .GetString() == AppFolderPath &&
                node.GetProperty("kind")
                    .GetString() == "folder");
        srcTreeNodes.EnumerateArray().Should().Contain(node =>
                node.GetProperty("path")
                    .GetString() == CoreFolderPath &&
                node.GetProperty("kind")
                    .GetString() == "folder");

        scopedNodes.EnumerateArray().Should().Contain(node =>
                node.GetProperty("id")
                    .GetInt32() == 100 &&
                node.GetProperty("scope")
                    .GetString() == "selected");
        scopedNodes.EnumerateArray().Should().Contain(node =>
                node.GetProperty("id")
                    .GetInt32() == 200 &&
                node.GetProperty("label")
                    .GetString() == "Fixture.App.HttpEndpoint.Handle()" &&
                node.GetProperty("codeNodeId")
                    .GetInt32() == 200 &&
                node.GetProperty("type")
                    .GetString() == "method" &&
                node.GetProperty("scope")
                    .GetString() == "selected");
        scopedNodes.EnumerateArray().Should().Contain(node =>
                node.GetProperty("id")
                    .GetInt32() == 101 &&
                node.GetProperty("scope")
                    .GetString() == "external");
        scopedNodes.EnumerateArray().Should().Contain(node =>
                node.GetProperty("id")
                    .GetInt32() == 201 &&
                node.GetProperty("label")
                    .GetString() == "Fixture.Core.MessageProvider.GetMessage()" &&
                node.GetProperty("type")
                    .GetString() == "method" &&
                node.GetProperty("scope")
                    .GetString() == "external");
        scopedEdges.EnumerateArray().Should().Contain(edge =>
                edge.GetProperty("source")
                    .GetInt32() == 100 &&
                edge.GetProperty("target")
                    .GetInt32() == 101 &&
                edge.GetProperty("type")
                    .GetString() == "projectreference" &&
                edge.GetProperty("scope")
                    .GetString() == "boundary");
        scopedEdges.EnumerateArray().Should().Contain(edge =>
                edge.GetProperty("source")
                    .GetInt32() == 200 &&
                edge.GetProperty("target")
                    .GetInt32() == 201 &&
                edge.GetProperty("type")
                    .GetString() == "methodcall" &&
                edge.GetProperty("scope")
                    .GetString() == "boundary");
    }

    [Fact]
    public async Task WhenWorkspaceIsIndexed_ThenOverviewReportsItsCodeNodeCount()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await UiHost.Start(ct);
        var httpClient = host.Client;
        var baseUrl = host.BaseUrl;

        using var overview = await httpClient.GetAsync(
            $"{baseUrl}/api/overview",
            ct);
        overview.StatusCode.Should().Be(HttpStatusCode.OK);
        using var overviewJson = JsonDocument.Parse(await overview.Content.ReadAsStringAsync(ct));
        overviewJson.RootElement.GetProperty("indexed")
            .GetBoolean().Should().BeTrue();
        overviewJson.RootElement.GetProperty("nodeCount")
            .GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task WhenOpenApiIsRequested_ThenDocumentsSupportedRoutesAndWorkspaceHeaders()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await UiHost.Start(ct);
        var httpClient = host.Client;
        var baseUrl = host.BaseUrl;

        using var openApi = await httpClient.GetAsync(
            $"{baseUrl}/openapi/v1.json",
            ct);
        openApi.StatusCode.Should().Be(HttpStatusCode.OK);
        using var schema = JsonDocument.Parse(await openApi.Content.ReadAsStringAsync(ct));
        schema.RootElement.GetProperty("paths")
            .TryGetProperty("/api/tools/search", out _).Should().BeTrue();
        foreach (var retired in new[] { "/api/graph/view", "/api/graph/nodes", "/api/graph/edges" })
        {
            schema.RootElement.GetProperty("paths")
                .TryGetProperty(retired, out _).Should().BeFalse();
        }
        var connectionsHeader = schema.RootElement.GetProperty("paths")
            .GetProperty("/api/graph/nodes/{nodeId}/connections")
            .GetProperty("get")
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => parameter.GetProperty("name")
                .GetString() == "X-SharpSense-Workspace");
        connectionsHeader.GetProperty("required")
            .GetBoolean().Should().BeTrue();
        connectionsHeader.GetProperty("schema")
            .GetProperty("format")
            .GetString().Should().Be("uuid");
        schema.RootElement.ToString().Should().Contain("codeNodeId");
        schema.RootElement.GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("WorkspaceOverview")
            .GetProperty("properties")
            .GetProperty("nodeCount")
            .GetProperty("type")
            .GetString().Should().Be("integer");
    }

    [Theory]
    [InlineData("/api/graph/view")]
    [InlineData("/api/graph/nodes")]
    [InlineData("/api/graph/edges")]
    [InlineData("/api/missing")]
    public async Task WhenApiRouteDoesNotExist_ThenReturnsProblemDetails(string route)
    {
        await using var host = await UiHost.Start(TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Theory]
    [InlineData("context", 200, "callees", 201)]
    [InlineData("trace", 200, "nodes", 201)]
    [InlineData("impact", 201, "impactedNodes", 200)]
    public async Task WhenGraphToolRuns_ThenReturnsTheRelatedSymbol(
        string tool,
        int nodeId,
        string resultProperty,
        int expectedNodeId)
    {
        await using var host = await UiHost.Start(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        using var response = await host.Client.PostAsJsonAsync($"/api/tools/{tool}", new
        {
            nodeId
        }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        result.RootElement.GetProperty(resultProperty)
            .EnumerateArray()
            .Select(node => node.GetProperty("id").GetInt32())
            .Should()
            .Equal(expectedNodeId);
    }

    [Fact]
    public async Task WhenMethodHasNoInheritors_ThenReturnsAnEmptyArray()
    {
        await using var host = await UiHost.Start(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        using var response = await host.Client.PostAsJsonAsync("/api/tools/inheritors", new
        {
            nodeId = 200
        }, ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        result.RootElement.EnumerateArray().Should().BeEmpty();
    }

    [Theory]
    [InlineData("context")]
    [InlineData("trace")]
    [InlineData("inheritors")]
    [InlineData("impact")]
    public async Task WhenGraphToolTargetsAnUnknownNode_ThenReturnsNotFound(string tool)
    {
        await using var host = await UiHost.Start(TestContext.Current.CancellationToken);

        using var response = await host.Client.PostAsJsonAsync(
            $"/api/tools/{tool}",
            new
            {
                nodeId = 999999
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task WhenSearchIsBlank_ThenReturnsBadRequest()
    {
        await using var host = await UiHost.Start(TestContext.Current.CancellationToken);

        using var response = await host.Client.PostAsJsonAsync(
            "/api/tools/search",
            new
            {
                query = "",
                limit = 10
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Theory]
    [InlineData("*", false)]
    [InlineData("MessageProvider(*", true)]
    [InlineData("UnknownColumn:MessageProvider*", true)]
    public async Task WhenSearchContainsPunctuation_ThenSearchesItsPlainTextTerms(string expression, bool hasMatch)
    {
        await using var host = await UiHost.Start(TestContext.Current.CancellationToken);
        var ct = TestContext.Current.CancellationToken;

        using var response = await host.Client.PostAsJsonAsync(
            "/api/tools/search",
            new
            {
                query = expression,
                limit = 10
            },
            ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        result.RootElement.GetProperty("searchText").GetString().Should().Be(expression);
        var ids = result.RootElement
            .GetProperty("hits")
            .EnumerateArray()
            .Select(hit => hit.GetProperty("id").GetInt32())
            .ToArray();
        if (hasMatch)
        {
            ids.Should().Contain(201);
        }
        else
        {
            ids.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhenTracingOwnedMembers_ThenExcludesStructuralEdgesAndMarksOnlyCodeParentsSelectable()
    {
        await using var database = await UiCommandTestDatabase.Create(TestContext.Current.CancellationToken);
        await database.AddStructuralRelationships(TestContext.Current.CancellationToken);
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("fixture", RepositoryRoot, []);
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                services.AddSingleton<IWorkspaceCatalog>(catalog);
                database.ConfigureServices(services);
            },
            enableFileLogging: false);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runTask = app.RunAsync(["ui", "--url", baseUrl, "--repo-root", RepositoryRoot], shutdown.Token);
        try
        {
            await WaitForServer(
                runTask,
                $"{baseUrl}/",
                TestContext.Current.CancellationToken);
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            client.DefaultRequestHeaders.Add("X-SharpSense-Workspace", selection.Definition.Id.ToString());
            var ct = TestContext.Current.CancellationToken;
            using var trace = await client.PostAsJsonAsync(
                $"{baseUrl}/api/tools/trace",
                new
                {
                    nodeId = 200,
                    direction = "callee",
                    maxDepth = 3
                },
                ct);
            trace.StatusCode.Should().Be(HttpStatusCode.OK);
            using var traceJson = JsonDocument.Parse(await trace.Content.ReadAsStringAsync(ct));
            var dependencies = traceJson.RootElement.GetProperty("dependencies")
                .EnumerateArray()
                .ToArray();
            dependencies.Length.Should().Be(2);
            dependencies.Should().NotContain(edge =>
                string.Equals(
                    edge.GetProperty("edgeType")
                        .GetString(),
                    "ParentOf",
                    StringComparison.OrdinalIgnoreCase));
            traceJson.RootElement.GetProperty("nodes")
                .EnumerateArray().Should().Contain(node => node.GetProperty("id")
                    .GetInt32() == 202);

            using var callers = await client.PostAsJsonAsync(
                $"{baseUrl}/api/tools/trace",
                new
                {
                    nodeId = 201,
                    direction = "CALLER",
                    maxDepth = 1
                },
                ct);
            callers.StatusCode.Should().Be(HttpStatusCode.OK);
            using var callersJson = JsonDocument.Parse(await callers.Content.ReadAsStringAsync(ct));
            callersJson.RootElement.GetProperty("root")
                .GetProperty("id")
                .GetInt32().Should().Be(201);
            callersJson.RootElement.GetProperty("direction")
                .GetString().Should().Be("caller");
            callersJson.RootElement.GetProperty("truncated")
                .GetBoolean().Should().BeFalse();
            var callerNode = callersJson.RootElement.GetProperty("nodes")
                .EnumerateArray().Should().ContainSingle().Which;
            callerNode.GetProperty("id")
                .GetInt32().Should().Be(200);
            callersJson.RootElement.GetProperty("dependencies")
                .EnumerateArray().Should().ContainSingle();

            foreach (var invalidTrace in new[]
            {
                new { nodeId = 0, direction = "callee", maxDepth = 3 },
                new { nodeId = 200, direction = "sideways", maxDepth = 3 },
                new { nodeId = 200, direction = "callee", maxDepth = 11 }
            })
            {
                using var invalidResponse = await client.PostAsJsonAsync(
                    $"{baseUrl}/api/tools/trace",
                    invalidTrace,
                    ct);
                invalidResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                (invalidResponse.Content.Headers.ContentType?.MediaType).Should().Be("application/problem+json");
            }

            using var invalidContext = await client.PostAsJsonAsync(
                $"{baseUrl}/api/tools/context",
                new
                {
                    nodeId = 0
                },
                ct);
            invalidContext.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            using var member = await client.PostAsJsonAsync(
                $"{baseUrl}/api/tools/context",
                new
                {
                    nodeId = 200
                },
                ct);
            using var memberJson = JsonDocument.Parse(await member.Content.ReadAsStringAsync(ct));
            var codeParent = memberJson.RootElement.GetProperty("parents")
                .EnumerateArray().Should().ContainSingle().Which;
            codeParent.GetProperty("codeNodeId")
                .GetInt32().Should().Be(202);

            using var owner = await client.PostAsJsonAsync(
                $"{baseUrl}/api/tools/context",
                new
                {
                    nodeId = 202
                },
                ct);
            using var ownerJson = JsonDocument.Parse(await owner.Content.ReadAsStringAsync(ct));
            var projectParent = ownerJson.RootElement.GetProperty("parents")
                .EnumerateArray().Should().ContainSingle().Which;
            projectParent.GetProperty("id")
                .GetInt32().Should().Be(100);
            (!projectParent.TryGetProperty(
                "codeNodeId",
                out var codeNodeId) || codeNodeId.ValueKind == JsonValueKind.Null).Should().BeTrue();
        }
        finally
        {
            shutdown.Cancel();
            // Cleanup must finish even when the test is cancelled.
            await runTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenGlobalUi_ThenStartsEmptyAndKeepsConcurrentWorkspaceRequestsAndMemoriesIsolated()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var firstDatabase = await UiCommandTestDatabase.Create(ct);
        await using var secondDatabase = await UiCommandTestDatabase.Create(ct);
        await secondDatabase.AddStructuralRelationships(ct);
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/backend/Orders.csproj"] = new("<Project />"),
                ["/repo/frontend/tsconfig.json"] = new("{}"),
                ["/repo/docs/design.md"] = new("# Architecture"),
                ["/repo/node_modules/ignored/tsconfig.json"] = new("{}"),
                ["/repo/.git/ignored.csproj"] = new("<Project />")
            },
            RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var firstId = Guid.Empty;
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                services.AddSingleton<IWorkspaceCatalog>(catalog);
                services.RemoveAll<IDbContextFactory<SharpSenseDbContext>>();
                services
                    .AddScoped(provider => provider.GetRequiredService<IRepositoryWorkspace>().WorkspaceId == firstId
                    ? firstDatabase.GetFactory()
                    : secondDatabase.GetFactory());
            },
            enableFileLogging: false);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        try
        {
            await WaitForServer(
                runTask,
                $"{baseUrl}/",
                ct);
            using var globalClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };
            using var emptyCatalog = JsonDocument.Parse(await globalClient.GetStringAsync("/api/workspaces", ct));
            emptyCatalog.RootElement.GetProperty("workspaces")
                .EnumerateArray().Should().BeEmpty();
            fileSystem.Directory.Exists(catalog.HomeDirectory).Should().BeFalse();

            globalClient.DefaultRequestHeaders.Add("Origin", baseUrl);
            using var discovery = await globalClient.PostAsJsonAsync(
                "/api/workspaces/discover",
                new
                {
                    repositoryRoot = RepositoryRoot
                },
                ct);
            discovery.StatusCode.Should().Be(HttpStatusCode.OK);
            using var candidates = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync(ct));
            candidates.RootElement.GetProperty("sources")
                .EnumerateArray()
                .Select(static source => source.GetProperty("path")
                    .GetString())
                .OrderBy(static path => path)
                .ToArray().Should().Equal(new[] { "backend/Orders.csproj", "docs/*.md", "frontend/tsconfig.json" });
            fileSystem.Directory.Exists(catalog.HomeDirectory).Should().BeFalse();

            firstId = await CreateWorkspace("first");
            var secondId = await CreateWorkspace("second");
            using var firstClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };
            using var secondClient = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };
            firstClient.DefaultRequestHeaders.Add("X-SharpSense-Workspace", firstId.ToString());
            secondClient.DefaultRequestHeaders.Add("X-SharpSense-Workspace", secondId.ToString());

            using var missingSelection = await globalClient.GetAsync("/api/overview", ct);
            missingSelection.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var unknownRequest = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
            unknownRequest.Headers.Add(
                "X-SharpSense-Workspace",
                Guid.NewGuid()
                    .ToString());
            using var unknownSelection = await globalClient.SendAsync(unknownRequest, ct);
            unknownSelection.StatusCode.Should().Be(HttpStatusCode.NotFound);

            var overviews = await Task.WhenAll(
                firstClient.GetStringAsync("/api/overview", ct),
                secondClient.GetStringAsync("/api/overview", ct));
            using var firstOverview = JsonDocument.Parse(overviews[0]);
            using var secondOverview = JsonDocument.Parse(overviews[1]);
            firstOverview.RootElement.GetProperty("workspaceId")
                .GetGuid().Should().Be(firstId);
            secondOverview.RootElement.GetProperty("workspaceId")
                .GetGuid().Should().Be(secondId);
            firstOverview.RootElement.GetProperty("nodeCount")
                .GetInt32().Should().Be(2);
            secondOverview.RootElement.GetProperty("nodeCount")
                .GetInt32().Should().Be(3);

            using var firstConnections = JsonDocument.Parse(await firstClient.GetStringAsync(
                "/api/graph/nodes/200/connections?includeTotal=true",
                ct));
            using var secondConnections = JsonDocument.Parse(await secondClient.GetStringAsync(
                "/api/graph/nodes/200/connections?includeTotal=true",
                ct));
            firstConnections.RootElement.GetProperty("totalCount")
                .GetInt32().Should().Be(1);
            secondConnections.RootElement.GetProperty("totalCount")
                .GetInt32().Should().Be(2);

            using var attached = await firstClient.PostAsJsonAsync(
                "/api/memory/node/200",
                new
                {
                    content = "Belongs only to first workspace",
                    tags = new[] { "scope" },
                    intent = "Invariant"
                },
                ct);
            attached.StatusCode.Should().Be(HttpStatusCode.OK);
            using var firstMemories = JsonDocument.Parse(await firstClient.GetStringAsync("/api/memory/node/200", ct));
            using var secondMemories = JsonDocument.Parse(await secondClient.GetStringAsync("/api/memory/node/200", ct));
            firstMemories.RootElement.EnumerateArray().Should().ContainSingle();
            secondMemories.RootElement.EnumerateArray().Should().BeEmpty();

            using var renamed = await globalClient.PutAsJsonAsync(
                $"/api/workspaces/{firstId}",
                new
                {
                    name = "renamed-first",
                    sources = Array.Empty<object>()
                },
                ct);
            renamed.StatusCode.Should().Be(HttpStatusCode.OK);
            catalog.Resolve("renamed-first").Definition.Id.Should().Be(firstId);

            using var schema = JsonDocument.Parse(await globalClient.GetStringAsync("/openapi/v1.json", ct));
            foreach (var path in schema.RootElement.GetProperty("paths")
                .EnumerateObject())
            {
                var scoped = !path.Name.StartsWith("/api/workspaces", StringComparison.Ordinal) && path.Name != "/api/tools";
                foreach (var operation in path.Value.EnumerateObject()
                    .Where(static property =>
                             property.Name is "get" or "post" or "put" or "patch" or "delete"))
                {
                    var headers = operation.Value.TryGetProperty("parameters", out var parameters)
                        ? parameters.EnumerateArray()
                            .Where(static parameter => parameter.TryGetProperty("name", out var name) &&
                            name.GetString() == "X-SharpSense-Workspace")
                            .ToArray()
                        : [];
                    if (!scoped)
                    {
                        headers.Should().BeEmpty();
                        continue;
                    }

                    var header = headers.Should().ContainSingle().Which;
                    header.GetProperty("in")
                        .GetString().Should().Be("header");
                    header.GetProperty("required")
                        .GetBoolean().Should().BeTrue();
                    header.GetProperty("schema")
                        .GetProperty("type")
                        .GetString().Should().Be("string");
                    header.GetProperty("schema")
                        .GetProperty("format")
                        .GetString().Should().Be("uuid");
                }
            }

            async Task<Guid> CreateWorkspace(string name)
            {
                using var response = await globalClient.PostAsJsonAsync(
                    "/api/workspaces",
                    new
                    {
                        name,
                        repositoryRoot = RepositoryRoot,
                        sources = Array.Empty<object>()
                    },
                    ct);
                response.StatusCode.Should().Be(HttpStatusCode.Created);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

                return json.RootElement.GetProperty("id")
                    .GetGuid();
            }
        }
        finally
        {
            shutdown.Cancel();
            // Cleanup must finish even when the test is cancelled.
            await runTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenGlobalUi_ThenRejectsCrossOriginAndUnexpectedHostBeforeCatalogWrites()
    {
        var ct = TestContext.Current.CancellationToken;
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                services.AddSingleton<IWorkspaceCatalog>(catalog);
            },
            enableFileLogging: false);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        try
        {
            await WaitForServer(
                runTask,
                $"{baseUrl}/",
                ct);
            using var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl)
            };
            foreach (var (header, value) in new[]
            {
                ("Origin", "https://untrusted.example"),
                ("Host", "untrusted.example"),
                ("Sec-Fetch-Site", "cross-site")
            })
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
                {
                    Content = JsonContent.Create(new
                    {
                        name = "blocked",
                        repositoryRoot = RepositoryRoot,
                        sources = Array.Empty<object>()
                    })
                };
                request.Headers.TryAddWithoutValidation(header, value);
                using var response = await client.SendAsync(request, ct);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }

            catalog.List().Should().BeEmpty();
            fileSystem.Directory.Exists(catalog.HomeDirectory).Should().BeFalse();
        }
        finally
        {
            shutdown.Cancel();
            // Cleanup must finish even when the test is cancelled.
            await runTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenAmbientKestrelEndpointConflicts_ThenListensOnlyAtSelectedUrl(bool useEnvironment)
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("sharpsense-ui-binding-");
        var previousDirectory = Environment.CurrentDirectory;
        const string endpointVariable = "Kestrel__Endpoints__Ambient__Url";
        var previousEndpoint = Environment.GetEnvironmentVariable(endpointVariable);
        using var occupiedEndpoint = new TcpListener(IPAddress.Loopback, 0);
        occupiedEndpoint.Start();
        var ambientUrl = $"http://127.0.0.1:{((IPEndPoint)occupiedEndpoint.LocalEndpoint).Port}";
        var selectedUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var fileSystem = new MockFileSystem();
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<int>? runTask = null;

        try
        {
            Environment.CurrentDirectory = directory.FullName;
            Environment.SetEnvironmentVariable(endpointVariable, useEnvironment ? ambientUrl : null);
            if (!useEnvironment)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory.FullName, "appsettings.json"),
                    JsonSerializer.Serialize(new
                    {
                        Kestrel = new
                        {
                            Endpoints = new
                            {
                                Ambient = new { Url = ambientUrl }
                            }
                        }
                    }),
                    ct);
            }
            var app = Cli.Program.CreateCommandApp(
                configureServices: services =>
                {
                    services.AddSingleton<IFileSystem>(fileSystem);
                    services.AddSingleton<IWorkspaceCatalog>(catalog);
                },
                enableFileLogging: false);

            runTask = app.RunAsync(["ui", "--url", selectedUrl], shutdown.Token);
            await WaitForServer(runTask, selectedUrl, ct);

            using var client = new HttpClient();
            using var response = await client.GetAsync($"{selectedUrl}/api/workspaces", ct);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var catalogResponse = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            catalogResponse.RootElement.GetProperty("workspaces")
                .EnumerateArray().Should().BeEmpty();
        }
        finally
        {
            try
            {
                await shutdown.CancelAsync();
                if (runTask is not null)
                {
                    // Cleanup must finish even when the test is cancelled.
                    await runTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
                }
            }
            finally
            {
                Environment.CurrentDirectory = previousDirectory;
                Environment.SetEnvironmentVariable(endpointVariable, previousEndpoint);
                directory.Delete(recursive: true);
            }
        }
    }

    private static async Task WaitForServer(Task<int> runTask, string url, CancellationToken ct)
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
                using var response = await httpClient.GetAsync(url, ct);
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

            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
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
        var ct = TestContext.Current.CancellationToken;
        await using var database = await UiCommandTestDatabase.Create(ct);
        var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            RepositoryRoot);
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("fixture", RepositoryRoot, []);
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                services.AddSingleton<IWorkspaceCatalog>(catalog);
                database.ConfigureServices(services);
            },
            enableFileLogging: false);
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var runTask = app.RunAsync(
            ["ui", "--url", baseUrl, "--repo-root", RepositoryRoot],
            shutdown.Token);

        try
        {
            await WaitForServer(
                runTask,
                $"{baseUrl}/",
                ct);

            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
            httpClient.DefaultRequestHeaders.Add("X-SharpSense-Workspace", selection.Definition.Id.ToString());

            const int nodeId = 200;
            var content = "Always greet politely: invariant the team expects.";

            using (var invalidTags = await httpClient.PostAsJsonAsync(
                $"{baseUrl}/api/memory/node/{nodeId}",
                new
                {
                    content,
                    tags = new string?[] { null }
                },
                ct))
            {
                invalidTags.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                invalidTags.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            }


            using (var addResponse = await httpClient.PostAsync(
                $"{baseUrl}/api/memory/node/{nodeId}",
                new StringContent(
                    $"{{\"content\":\"{content}\",\"tags\":[\"convention\"],\"intent\":\"Invariant\"}}",
                    System.Text.Encoding.UTF8,
                    "application/json"),
                ct))
            {
                addResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            var listJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/node/{nodeId}",
                ct);
            using var listDocument = JsonDocument.Parse(listJson);
            var memories = listDocument.RootElement.EnumerateArray()
                .ToArray();
            memories.Should().ContainSingle();
            var memoryId = memories[0].GetProperty("id")
                .GetString()!;
            memories[0].GetProperty("intent")
                .GetString().Should().Be("Invariant");

            var singleJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/{memoryId}",
                ct);
            using var singleDocument = JsonDocument.Parse(singleJson);
            singleDocument.RootElement.GetProperty("content")
                .GetString().Should().Be(content);

            var batchJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory?ids={memoryId}",
                ct);
            using var batchDocument = JsonDocument.Parse(batchJson);
            batchDocument.RootElement.GetArrayLength().Should().Be(1);

            using var invalidIntent = await httpClient.GetAsync(
                $"{baseUrl}/api/memory/node/{nodeId}?intents=Unknown",
                ct);
            invalidIntent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (invalidIntent.Content.Headers.ContentType?.MediaType).Should().Be("application/problem+json");

            var filteredJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/node/{nodeId}?intents=Invariant",
                ct);
            using var filteredDocument = JsonDocument.Parse(filteredJson);
            filteredDocument.RootElement.GetArrayLength().Should().Be(1);

            var noneJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/node/{nodeId}?intents=Warning",
                ct);
            using var noneDocument = JsonDocument.Parse(noneJson);
            noneDocument.RootElement.GetArrayLength().Should().Be(0);

            using (var deleteResponse = await httpClient.DeleteAsync(
                $"{baseUrl}/api/memory/{memoryId}",
                ct))
            {
                deleteResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            var afterJson = await httpClient.GetStringAsync(
                $"{baseUrl}/api/memory/node/{nodeId}",
                ct);
            using var afterDocument = JsonDocument.Parse(afterJson);
            afterDocument.RootElement.GetArrayLength().Should().Be(0);
        }
        finally
        {
            shutdown.Cancel();
            try
            {
                // Cleanup must finish even when the test is cancelled.
                await runTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }
    }

    private sealed class UiHost(
        UiCommandTestDatabase database,
        string baseUrl,
        HttpClient client,
        CancellationTokenSource shutdown,
        Task<int> runTask) : IAsyncDisposable
    {
        public string BaseUrl { get; } = baseUrl;
        public HttpClient Client { get; } = client;

        public static async Task<UiHost> Start(CancellationToken ct)
        {
            var database = await UiCommandTestDatabase.Create(ct);
            var baseUrl = $"http://127.0.0.1:{GetAvailablePort()}";
            var fileSystem = new MockFileSystem(
                new Dictionary<string, MockFileData>
                {
                    ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
                },
                RepositoryRoot);
            var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
            var selection = catalog.Create("fixture", RepositoryRoot, []);
            var app = Cli.Program.CreateCommandApp(
                configureServices: services =>
                {
                    services.AddSingleton<IFileSystem>(fileSystem);
                    services.AddSingleton<IWorkspaceCatalog>(catalog);
                    database.ConfigureServices(services);
                },
                enableFileLogging: false);
            var shutdown = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var runTask = app.RunAsync(["ui", "--url", baseUrl, "--repo-root", RepositoryRoot], shutdown.Token);
            var client = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(5)
            };
            client.DefaultRequestHeaders.Add("X-SharpSense-Workspace", selection.Definition.Id.ToString());
            var host = new UiHost(database, baseUrl, client, shutdown, runTask);
            try
            {
                await WaitForServer(runTask, baseUrl + "/", ct);

                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await shutdown.CancelAsync();
                // Cleanup must finish even when the test is cancelled.
                await runTask.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
            }
            finally
            {
                Client.Dispose();
                shutdown.Dispose();
                await database.DisposeAsync();
            }
        }
    }

    private sealed class UiCommandTestDatabase(InMemoryContextFactory<SharpSenseDbContext> contextFactory) : IAsyncDisposable
    {
        public const string AppProjectId = "project:src/Fixture.App/Fixture.App.csproj";
        public const string CoreProjectId = "project:src/Fixture.Core/Fixture.Core.csproj";
        public const string CallerCanonicalId = "code:project:src/Fixture.App/Fixture.App.csproj:Fixture.App.HttpEndpoint.Handle()";
        public const string CalleeCanonicalId = "code:project:src/Fixture.Core/Fixture.Core.csproj:Fixture.Core.MessageProvider.GetMessage()";

        public static async Task<UiCommandTestDatabase> Create(CancellationToken ct)
        {
            var contextFactory = new InMemoryContextFactory<SharpSenseDbContext>(
                options => new SharpSenseDbContext(options),
                new InMemoryContextFactoryOptions(UseMigrations: true, LoadVectorExtension: true));
            var database = new UiCommandTestDatabase(contextFactory);
            try
            {
                await database.Initialize(ct);

                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public void ConfigureServices(IServiceCollection services)
            => contextFactory.ConfigureServices(services);

        public IDbContextFactory<SharpSenseDbContext> GetFactory() => contextFactory.CreateDbContextFactory();

        public async ValueTask DisposeAsync()
        {
            await contextFactory.DisposeAsync();
        }

        public async Task AddStructuralRelationships(CancellationToken ct)
        {
            await using var context = await contextFactory.GetContext(ct);
            context.GraphNodes.Add(new GraphNodeRecord
            {
                Id = 202,
                CanonicalId = "code:Fixture.App:Fixture.App.HttpEndpoint",
                Kind = GraphNodeKind.Code
            });
            context.CodeNodes.Add(new CodeNodeRecord
            {
                Id = 202,
                ProjectNodeId = 100,
                DocumentId = 12,
                FullyQualifiedName = "Fixture.App.HttpEndpoint",
                DisplayName = "HttpEndpoint",
                NodeType = NodeType.Class,
                StartLine = 1,
                EndLine = 15,
                Summary = "Owns endpoint handler."
            });
            context.DependencyEdges.AddRange(
                new DependencyEdgeRecord
                {
                    CallerNodeId = 100,
                    CalleeNodeId = 202,
                    EdgeType = EdgeType.ParentOf
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = 202,
                    CalleeNodeId = 200,
                    EdgeType = EdgeType.ParentOf
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = 200,
                    CalleeNodeId = 202,
                    EdgeType = EdgeType.Instantiates
                });
            await context.SaveChangesAsync(ct);
        }

        private async Task Initialize(CancellationToken ct)
        {
            await using var dbContext = await contextFactory.GetContext(ct: ct);

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

            await dbContext.SaveChangesAsync(ct);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, SearchText, RelativeFilePath)
                SELECT CodeNodes.Id, GraphNodes.CanonicalId, CodeNodes.DisplayName,
                       CodeNodes.FullyQualifiedName, CodeNodes.SearchText, Documents.RelativePath
                FROM CodeNodes
                JOIN GraphNodes ON GraphNodes.Id = CodeNodes.Id
                JOIN Documents ON Documents.Id = CodeNodes.DocumentId;
                """,
                ct);
        }
    }
}
