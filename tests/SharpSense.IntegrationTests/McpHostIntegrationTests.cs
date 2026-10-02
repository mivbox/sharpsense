using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.Text.Json;

namespace SharpSense.IntegrationTests;

public sealed class McpHostIntegrationTests
{
    [Theory]
    [InlineData(false, "missing")]
    [InlineData(true, "incompatible")]
    public async Task WhenDatabaseNeedsAttention_ThenMcpStartsAndReportsDiagnosticsWithoutChangingIt(bool incompatible, string expectedState)
    {
        var directory = Directory.CreateTempSubdirectory("sharpsense-mcp-host-");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;

        try
        {
            var home = Path.Combine(directory.FullName, "home");
            var catalog = new WorkspaceCatalog(new FileSystem(), home);
            var selection = catalog.Create("fixture", directory.FullName, []);
            var databasePath = selection.Workspace.DatabasePath;
            byte[]? original = null;
            if (incompatible)
            {
                await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Pooling = false
                }.ToString());
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE AuthoredNotes (Content TEXT); INSERT INTO AuthoredNotes VALUES ('Keep this note');";
                await command.ExecuteNonQueryAsync(ct);
                await connection.CloseAsync();
                original = await File.ReadAllBytesAsync(databasePath, ct);
            }

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = "dotnet",
                Arguments = [typeof(Cli.Program).Assembly.Location, "mcp", "--workspace", "fixture"],
                WorkingDirectory = directory.FullName,
                EnvironmentVariables = new Dictionary<string, string?> { ["SHARPSENSE_HOME"] = home }
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: ct);

            var unknownCall = async () => await client.CallToolAsync("unknown_tool", cancellationToken: ct);
            await unknownCall.Should().ThrowAsync<ModelContextProtocol.McpException>();
            if (original is not null)
            {
                (await File.ReadAllBytesAsync(databasePath, ct)).Should().Equal(original);
            }
            else
            {
                File.Exists(databasePath).Should().BeFalse();
            }

            var response = await client.CallToolAsync("graph_stats", cancellationToken: ct);

            response.IsError.Should().NotBe(true);
            using var stats = JsonDocument.Parse(response.Content.OfType<TextContentBlock>().Single().Text);
            stats.RootElement.GetProperty("databaseState").GetString().Should().Be(expectedState);
            if (original is not null)
            {
                (await File.ReadAllBytesAsync(databasePath, ct)).Should().Equal(original);
            }
            else
            {
                File.Exists(databasePath).Should().BeFalse();
                var search = await client.CallToolAsync(
                    "semantic_search",
                    new Dictionary<string, object?> { ["query"] = "missing" },
                    cancellationToken: ct);
                search.IsError.Should().NotBe(true);
                File.Exists(databasePath).Should().BeTrue();
            }

            var tools = await client.ListToolsAsync(cancellationToken: ct);
            string[] readTools =
            [
                "graph_stats",
                "semantic_search",
                "context",
                "trace_node",
                "get_inheritors",
                "get_memory",
                "get_memories"
            ];
            tools.Where(tool => readTools.Contains(tool.Name))
                .Should().HaveCount(readTools.Length)
                .And.AllSatisfy(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint.Should().BeTrue(tool.Name));
            tools.Where(tool => !readTools.Contains(tool.Name))
                .Should().AllSatisfy(tool => tool.ProtocolTool.Annotations?.ReadOnlyHint.Should().NotBe(true, tool.Name));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
