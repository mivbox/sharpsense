using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceIndexingHttpEventsTests
{
    [Fact]
    public async Task HttpStreamUsesTypedStatusContractAndReconnectsWithAuthoritativeSnapshot()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, "/repo");
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
        await using var coordinator = new WorkspaceIndexingCoordinator(async (_, _, update, token) =>
        {
            publish = update;
            snapshots.Notify(new(operationId, 1, observedAt, AnalysisNotificationKind.Started, AnalysisOperationKind.Full));
            snapshots.Notify(new(operationId, 2, observedAt, AnalysisNotificationKind.PhaseChanged, AnalysisOperationKind.Full,
                Phase: AnalysisPhase.Extraction));
            snapshots.Notify(new(operationId, 3, observedAt, AnalysisNotificationKind.SourceStarted, AnalysisOperationKind.Full,
                Phase: AnalysisPhase.Extraction, Source: source));
            snapshots.Notify(new(operationId, 4, observedAt, AnalysisNotificationKind.SourceProgress, AnalysisOperationKind.Full,
                Phase: AnalysisPhase.Extraction, Source: source, Message: "Analyzing declarations", CompletedItems: 3, TotalItems: 10));
            update(new WorkspaceIndexingUpdate("indexing", "Analyzing declarations", Analysis: snapshots.Snapshot));
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, CancellationToken.None, NullLogger<WorkspaceIndexingCoordinator>.Instance);
        var baseUrl = $"http://127.0.0.1:{AvailablePort()}";
        var app = Cli.Program.CreateCommandApp(configureServices: services =>
        {
            services.AddSingleton<IFileSystem>(fileSystem);
            services.AddSingleton(catalog);
            services.AddSingleton(coordinator);
        }, enableFileLogging: false);
        using var shutdown = new CancellationTokenSource();
        var runTask = app.RunAsync(["ui", "--url", baseUrl], shutdown.Token);
        using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };

        try
        {
            await WaitForServer(client, path, runTask, ct);
            string previousEventId;
            using (var response = await client.GetAsync(path + "/events", HttpCompletionOption.ResponseHeadersRead, ct))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
                var initial = await ReadFrame(reader, ct);
                Assert.Equal("status", initial.Type);
                Assert.Equal("idle", initial.Data.GetProperty("state").GetString());
                Assert.Equal(workspaceId, initial.Data.GetProperty("workspaceId").GetGuid());
                Assert.Equal($"{initial.Data.GetProperty("streamId").GetGuid()}:0", initial.Id);

                using var start = await client.PostAsJsonAsync(path, new StartWorkspaceIndexingRequest(Watch: true), ct);
                Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
                await started.Task.WaitAsync(ct);
                var active = await ReadFrame(reader, ct);
                while (active.Data.GetProperty("analysis").ValueKind == JsonValueKind.Null)
                {
                    active = await ReadFrame(reader, ct);
                }

                Assert.Equal("status", active.Type);
                Assert.Equal("indexing", active.Data.GetProperty("state").GetString());
                Assert.Equal($"{active.Data.GetProperty("streamId").GetGuid()}:{active.Data.GetProperty("sequence").GetInt64()}", active.Id);
                Assert.True(active.Data.GetProperty("startedAt").TryGetDateTimeOffset(out _));
                Assert.True(active.Data.GetProperty("updatedAt").TryGetDateTimeOffset(out _));
                var analysis = active.Data.GetProperty("analysis");
                Assert.Equal(operationId, analysis.GetProperty("operationId").GetGuid());
                Assert.Equal("Full", analysis.GetProperty("operationKind").GetString());
                Assert.Equal("Extraction", analysis.GetProperty("phase").GetString());
                Assert.Equal(observedAt, analysis.GetProperty("startedAt").GetDateTimeOffset());
                var row = Assert.Single(analysis.GetProperty("sources").EnumerateArray());
                Assert.Equal("CSharp", row.GetProperty("kind").GetString());
                Assert.Equal("App.csproj", row.GetProperty("path").GetString());
                Assert.Equal(3, row.GetProperty("completedItems").GetInt32());
                Assert.Equal(10, row.GetProperty("totalItems").GetInt32());
                using var current = JsonDocument.Parse(await client.GetStringAsync(path, ct));
                Assert.True(JsonElement.DeepEquals(current.RootElement, active.Data));
                previousEventId = active.Id;
            }

            // Closing a response releases only its subscriber. The same job continues and
            // the next connection receives its latest committed state rather than replay.
            Assert.Equal("indexing", coordinator.GetStatus(workspaceId).State);
            var summary = new AnalysisSummary(1, 10, 20, 0, 1, 0, 4, 6, 0);
            snapshots.Notify(new(operationId, 5, observedAt.AddSeconds(1), AnalysisNotificationKind.Committed,
                AnalysisOperationKind.Full, Phase: AnalysisPhase.Persistence, Summary: summary));
            publish!(new WorkspaceIndexingUpdate("watching", "Watching workspace sources for changes.",
                IndexCommitted: true, Analysis: snapshots.Snapshot));
            using (var reconnect = new HttpRequestMessage(HttpMethod.Get, path + "/events"))
            {
                reconnect.Headers.Add("Last-Event-ID", previousEventId);
                using var response = await client.SendAsync(reconnect, HttpCompletionOption.ResponseHeadersRead, ct);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
                var latest = await ReadFrame(reader, ct);
                Assert.Equal("status", latest.Type);
                Assert.NotEqual(previousEventId, latest.Id);
                Assert.Equal("watching", latest.Data.GetProperty("state").GetString());
                Assert.Equal(1, latest.Data.GetProperty("revision").GetInt64());
                Assert.Equal("completed", latest.Data.GetProperty("analysis").GetProperty("state").GetString());
                Assert.Equal(10, latest.Data.GetProperty("analysis").GetProperty("lastCommittedSummary").GetProperty("nodes").GetInt32());
                using var current = JsonDocument.Parse(await client.GetStringAsync(path, ct));
                Assert.True(JsonElement.DeepEquals(current.RootElement, latest.Data));
            }

            using var openApi = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json", ct));
            var paths = openApi.RootElement.GetProperty("paths");
            var stream = paths.GetProperty("/api/workspaces/{workspaceId}/indexing/events").GetProperty("get");
            Assert.Equal("StreamWorkspaceIndexingStatus", stream.GetProperty("operationId").GetString());
            var streamSchema = stream.GetProperty("responses").GetProperty("200").GetProperty("content")
                .GetProperty("text/event-stream").GetProperty("schema");
            var statusSchema = paths.GetProperty("/api/workspaces/{workspaceId}/indexing").GetProperty("get")
                .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");
            Assert.Equal(statusSchema.GetProperty("$ref").GetString(), streamSchema.GetProperty("$ref").GetString());
            Assert.False(fileSystem.File.Exists(selection.Workspace.DatabasePath));
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
                case "event": type = value; break;
                case "id": id = value; break;
                case "data": data.Add(value); break;
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
