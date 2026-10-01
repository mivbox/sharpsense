using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceIndexingHttpEventsTests
{
    [Fact]
    public async Task WhenHttpStream_ThenUsesTypedStatusContractAndReconnectsWithAuthoritativeSnapshot()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            "/repo");
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("sse-fixture", "/repo", []);
        var workspaceId = selection.Definition.Id;
        var path = $"/api/workspaces/{workspaceId}/indexing";
        var operationId = Guid.NewGuid();
        var source = new AnalysisSource(WorkspaceSourceKind.CSharp, "App.csproj");
        var snapshots = new AnalysisSnapshotStore();
        var observedAt = DateTimeOffset.UtcNow;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<WorkspaceIndexingUpdate>? publish = null;
        await using var coordinator = new WorkspaceIndexingCoordinator(
            async (_, _, update, token) =>
            {
                publish = update;
                snapshots.Notify(new(
                    operationId,
                    1,
                    observedAt,
                    AnalysisNotificationKind.Started,
                    AnalysisOperationKind.Full));
                snapshots.Notify(new(
                    operationId,
                    2,
                    observedAt,
                    AnalysisNotificationKind.PhaseChanged,
                    AnalysisOperationKind.Full,
                    Phase: AnalysisPhase.Extraction));
                snapshots.Notify(new(
                    operationId,
                    3,
                    observedAt,
                    AnalysisNotificationKind.SourceStarted,
                    AnalysisOperationKind.Full,
                    Phase: AnalysisPhase.Extraction,
                    Source: source));
                snapshots.Notify(new(
                    operationId,
                    4,
                    observedAt,
                    AnalysisNotificationKind.SourceProgress,
                    AnalysisOperationKind.Full,
                    Phase: AnalysisPhase.Extraction,
                    Source: source,
                    Message: "Analyzing declarations",
                    CompletedItems: 3,
                    TotalItems: 10));
                update(new WorkspaceIndexingUpdate("indexing", "Analyzing declarations", Analysis: snapshots.Snapshot));
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
            CancellationToken.None,
            NullLogger<WorkspaceIndexingCoordinator>.Instance);
        var baseUrl = $"http://127.0.0.1:{AvailablePort()}";
        var app = Cli.Program.CreateCommandApp(
            configureServices: services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                services.AddSingleton<IWorkspaceCatalog>(catalog);
                services.AddSingleton(coordinator);
            },
            enableFileLogging: false);
        using var shutdown = new CancellationTokenSource();
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        using var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };

        try
        {
            await WaitForServer(client, path, runTask, ct);
            string previousEventId;
            using (var response = await client.GetAsync(path + "/events", HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                (response.Content.Headers.ContentType?.MediaType).Should().Be("text/event-stream");
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
                var initial = await ReadFrame(reader, ct);
                initial.Type.Should().Be("status");
                initial.Data.GetProperty("state")
                    .GetString().Should().Be("idle");
                initial.Data.GetProperty("workspaceId")
                    .GetGuid().Should().Be(workspaceId);
                initial.Id.Should().Be($"{initial.Data.GetProperty("streamId")
                    .GetGuid()}:0");

                using var start = await client.PostAsJsonAsync(
                    path,
                    new StartWorkspaceIndexingRequest(Watch: true),
                    ct);
                start.StatusCode.Should().Be(HttpStatusCode.Accepted);
                await started.Task.WaitAsync(ct);
                var active = await ReadFrame(reader, ct);
                while (active.Data.GetProperty("analysis").ValueKind == JsonValueKind.Null)
                {
                    active = await ReadFrame(reader, ct);
                }

                active.Type.Should().Be("status");
                active.Data.GetProperty("state")
                    .GetString().Should().Be("indexing");
                active.Id.Should().Be($"{active.Data.GetProperty("streamId")
                    .GetGuid()}:{active.Data.GetProperty("sequence")
                        .GetInt64()}");
                active.Data.GetProperty("startedAt")
                    .TryGetDateTimeOffset(out _).Should().BeTrue();
                active.Data.GetProperty("updatedAt")
                    .TryGetDateTimeOffset(out _).Should().BeTrue();
                var analysis = active.Data.GetProperty("analysis");
                analysis.GetProperty("operationId")
                    .GetGuid().Should().Be(operationId);
                analysis.GetProperty("operationKind")
                    .GetString().Should().Be("Full");
                analysis.GetProperty("phase")
                    .GetString().Should().Be("Extraction");
                analysis.GetProperty("startedAt")
                    .GetDateTimeOffset().Should().Be(observedAt);
                var row = analysis.GetProperty("sources")
                    .EnumerateArray().Should().ContainSingle().Which;
                row.GetProperty("kind")
                    .GetString().Should().Be("CSharp");
                row.GetProperty("path")
                    .GetString().Should().Be("App.csproj");
                row.GetProperty("completedItems")
                    .GetInt32().Should().Be(3);
                row.GetProperty("totalItems")
                    .GetInt32().Should().Be(10);
                using var current = JsonDocument.Parse(await client.GetStringAsync(path, ct));
                JsonElement.DeepEquals(current.RootElement, active.Data).Should().BeTrue();
                previousEventId = active.Id;
            }

            // Closing a response releases only its subscriber. The same job continues and
            // the next connection receives its latest committed state rather than replay.
            coordinator.GetStatus(workspaceId).State.Should().Be("indexing");
            var summary = new AnalysisSummary(1, 10, 20, 0, 1, 0, 4, 6, 0);
            snapshots.Notify(new(
                operationId,
                5,
                observedAt.AddSeconds(1),
                AnalysisNotificationKind.Committed,
                AnalysisOperationKind.Full,
                Phase: AnalysisPhase.Persistence,
                Summary: summary));
            publish!(new WorkspaceIndexingUpdate(
                "watching",
                "Watching workspace sources for changes.",
                IndexCommitted: true,
                Analysis: snapshots.Snapshot));
            using (var reconnect = new HttpRequestMessage(HttpMethod.Get, path + "/events"))
            {
                reconnect.Headers.Add("Last-Event-ID", previousEventId);
                using var response = await client.SendAsync(reconnect, HttpCompletionOption.ResponseHeadersRead, ct);
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
                var latest = await ReadFrame(reader, ct);
                latest.Type.Should().Be("status");
                latest.Id.Should().NotBe(previousEventId);
                latest.Data.GetProperty("state")
                    .GetString().Should().Be("watching");
                latest.Data.GetProperty("revision")
                    .GetInt64().Should().Be(1);
                latest.Data.GetProperty("analysis")
                    .GetProperty("state")
                    .GetString().Should().Be("completed");
                latest.Data.GetProperty("analysis")
                    .GetProperty("lastCommittedSummary")
                    .GetProperty("nodes")
                    .GetInt32().Should().Be(10);
                using var current = JsonDocument.Parse(await client.GetStringAsync(path, ct));
                JsonElement.DeepEquals(current.RootElement, latest.Data).Should().BeTrue();
            }

            using var openApi = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", ct));
            var paths = openApi.RootElement.GetProperty("paths");
            var stream = paths.GetProperty("/api/workspaces/{workspaceId}/indexing/events")
                .GetProperty("get");
            stream.GetProperty("operationId")
                .GetString().Should().Be("StreamWorkspaceIndexingStatus");
            var streamSchema = stream.GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("text/event-stream")
                .GetProperty("schema");
            var statusSchema = paths.GetProperty("/api/workspaces/{workspaceId}/indexing")
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content")
                .GetProperty("application/json")
                .GetProperty("schema");
            streamSchema.GetProperty("$ref")
                .GetString().Should().Be(statusSchema.GetProperty("$ref")
                    .GetString());
            fileSystem.File.Exists(selection.Workspace.DatabasePath).Should().BeFalse();
        }
        finally
        {
            await shutdown.CancelAsync();
            await runTask.WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        }
    }

    private static async Task<EventFrame> ReadFrame(StreamReader reader, CancellationToken ct)
    {
        string? type = null;
        string? id = null;
        var data = new List<string>();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Count == 0)
                {
                    continue;
                }

                using var document = JsonDocument.Parse(string.Join('\n', data));

                return new EventFrame(type ?? "message", id ?? string.Empty, document.RootElement.Clone());
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var field = line[..separator];
            var value = line[(separator + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }
            switch (field)
            {
                case "event":
                    type = value;
                    break;
                case "id":
                    id = value;
                    break;
                case "data":
                    data.Add(value);
                    break;
            }
        }

        throw new EndOfStreamException("SSE response ended before a complete event.");
    }

    private static async Task WaitForServer(HttpClient client, string path, Task<int> runTask, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (runTask.IsCompleted)
            {
                throw new InvalidOperationException($"UI command exited before accepting requests: {await runTask}.");
            }
            try
            {
                using var response = await client.GetAsync(path, ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The loopback listener may not be bound yet.
            }
            await Task.Delay(TimeSpan.FromMilliseconds(50), ct);
        }

        ct.ThrowIfCancellationRequested();
    }

    private static int AvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed record EventFrame(string Type, string Id, JsonElement Data);
}
