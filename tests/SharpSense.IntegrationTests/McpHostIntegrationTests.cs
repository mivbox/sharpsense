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

            var resources = await client.ListResourcesAsync(cancellationToken: ct);
            resources.Select(resource => resource.Uri).Should().BeEquivalentTo(
                [
                    "sharpsense://workspace/stats",
                    "sharpsense://workspace/memories",
                    "sharpsense://workspace/memories/stale"
                ]);

            var templates = await client.ListResourceTemplatesAsync(cancellationToken: ct);
            templates.Select(template => template.UriTemplate).Should().BeEquivalentTo(
                [
                    "sharpsense://node/{nodeId}/context"
                ]);

            var prompts = await client.ListPromptsAsync(cancellationToken: ct);
            prompts.Select(prompt => prompt.Name).Should().BeEquivalentTo(
                [
                    "explain_symbol",
                    "impact_review",
                    "memory_audit"
                ]);

            var statsResource = await client.ReadResourceAsync("sharpsense://workspace/stats", cancellationToken: ct);
            var statsResourceText = statsResource.Contents.OfType<TextResourceContents>().Single().Text;
            using var resourceStats = JsonDocument.Parse(statsResourceText);
            resourceStats.RootElement.GetProperty("databaseState").GetString().Should().Be(expectedState);

            var unknownResource = async () => await client.ReadResourceAsync("sharpsense://workspace/unknown", cancellationToken: ct);
            await unknownResource.Should().ThrowAsync<ModelContextProtocol.McpException>();

            var invalidNodeResource = async () => await client.ReadResourceAsync("sharpsense://node/0/context", cancellationToken: ct);
            await invalidNodeResource.Should().ThrowAsync<ModelContextProtocol.McpException>();

            var promptResult = await client.GetPromptAsync(
                "impact_review",
                new Dictionary<string, object?>
                {
                    ["target"] = "WorkspaceCatalog.Create",
                    ["changeDescription"] = "Add optional validation parameter"
                },
                cancellationToken: ct);
            promptResult.Messages.Should().ContainSingle();
            promptResult.Messages[0].Content.Should().BeOfType<TextContentBlock>()
                .Which.Text.Should().Contain("WorkspaceCatalog.Create")
                .And.Contain("Add optional validation parameter");

            if (original is not null)
            {
                (await File.ReadAllBytesAsync(databasePath, ct)).Should().Equal(original);
            }
            else
            {
                File.Exists(databasePath).Should().BeFalse();

                var memoriesResource = await client.ReadResourceAsync("sharpsense://workspace/memories", cancellationToken: ct);
                memoriesResource.Contents.OfType<TextResourceContents>().Should().ContainSingle()
                    .Which.Text.Should().Be("memories: 0 of 0 (stale: 0, has_more: false)");
                File.Exists(databasePath).Should().BeTrue();

                var staleMemoriesResource = await client.ReadResourceAsync("sharpsense://workspace/memories/stale", cancellationToken: ct);
                staleMemoriesResource.Contents.OfType<TextResourceContents>().Should().ContainSingle()
                    .Which.Text.Should().Be("memories: 0 of 0 (stale: 0, has_more: false)");

                var missingNodeResource = async () => await client.ReadResourceAsync("sharpsense://node/999/context", cancellationToken: ct);
                await missingNodeResource.Should().ThrowAsync<ModelContextProtocol.McpException>();

                var search = await client.CallToolAsync(
                    "semantic_search",
                    new Dictionary<string, object?> { ["query"] = "missing" },
                    cancellationToken: ct);
                search.IsError.Should().NotBe(true);
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
                "get_memories",
                "list_memories"
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
