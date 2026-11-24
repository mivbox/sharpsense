using AwesomeAssertions;
using FluentResults;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Moq;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.GraphStats.GetGraphStats.Models;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Memory.GetNodeMemories.Models;
using SharpSense.Application.Memory.ListMemories.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Errors;
using SharpSense.Cli.Mcp;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using System.Text.Json;
using Result = FluentResults.Result;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseMcpResourcesAndPromptsTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("sharpsense://workspace/stats", false)]
    [InlineData("sharpsense://workspace/unknown", false)]
    [InlineData("sharpsense://node/0/context", false)]
    [InlineData("sharpsense://node/-1/context", false)]
    [InlineData("sharpsense://node/abc/context", false)]
    [InlineData("sharpsense://workspace/memories", true)]
    [InlineData("sharpsense://workspace/memories/stale", true)]
    [InlineData("sharpsense://node/42/context", true)]
    public void WhenCheckingResourceDatabaseInitialization_ThenOnlyValidGraphResourcesRequireIt(
        string? uri,
        bool expected)
    {
        SharpSenseMcpResources.RequiresDatabaseInitialization(uri).Should().Be(expected);
    }

    [Fact]
    public async Task WhenWorkspaceStatsResourceIsRead_ThenItReturnsCamelCaseJsonSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new Mock<IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(It.IsAny<GetGraphStatsQuery>(), ct))
            .ReturnsAsync(new GraphStatsSnapshot(
                "/repo",
                "/home/workspaces/1/graph.db",
                "ready",
                true,
                12,
                10,
                15,
                4,
                1,
                10,
                2,
                [new LanguageStats("C#", 4, 10, 10)],
                [new EdgeTypeStats("MethodCall", 15)],
                null,
                null,
                [],
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "fixture"));

        var resource = await SharpSenseMcpResources.GetWorkspaceStats(handler.Object, ct);

        resource.Uri.Should().Be("sharpsense://workspace/stats");
        resource.MimeType.Should().Be("application/json");
        using var document = JsonDocument.Parse(resource.Text);
        document.RootElement.GetProperty("databaseState").GetString().Should().Be("ready");
        document.RootElement.GetProperty("workspaceName").GetString().Should().Be("fixture");
        document.RootElement.GetProperty("memoryCount").GetInt64().Should().Be(2);
    }

    [Fact]
    public async Task WhenStaleMemoriesResourceIsRead_ThenItRequestsStaleOnlyWithDefaultLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        var memoryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new Mock<IQueryHandler<ListMemoriesQuery, Result<ListMemoriesResult>>>(MockBehavior.Strict);
        handler
            .Setup(candidate => candidate.Handle(
                It.Is<ListMemoriesQuery>(query => query.StaleOnly && query.Limit == 100 && query.Offset == 0),
                ct))
            .ReturnsAsync(Result.Ok(new ListMemoriesResult(
                [
                    new MemoryNodeSummary(
                        memoryId,
                        42,
                        "Fixture.Service.Run()",
                        "src/Fixture/Service.cs",
                        MemoryIntent.Invariant,
                        ["sqlite"],
                        true,
                        DateTimeOffset.UtcNow,
                        "Keep read transaction deferred.")
                ],
                1,
                1,
                false)));

        var resource = await SharpSenseMcpResources.GetStaleMemories(handler.Object, ct);

        resource.Uri.Should().Be("sharpsense://workspace/memories/stale");
        resource.MimeType.Should().Be("text/plain");
        resource.Text.Should().Contain(memoryId.ToString())
            .And.Contain("stale=true")
            .And.Contain("Keep read transaction deferred.");
    }

    [Fact]
    public async Task WhenNodeContextResourceIsRead_ThenItIncludesStructuralAndSemanticContext()
    {
        var ct = TestContext.Current.CancellationToken;
        var memoryId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var contextHandler = new Mock<IQueryHandler<GetNodeContextQuery, Result<Context360Result>>>(MockBehavior.Strict);
        var memoryHandler = new Mock<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>(MockBehavior.Strict);
        contextHandler
            .Setup(candidate => candidate.Handle(new GetNodeContextQuery(42, 10), ct))
            .ReturnsAsync(Result.Ok(new Context360Result(
                new Context360Node(42, "Fixture.Worker.Execute()", NodeType.Method, "src/Worker.cs", 10, 25),
                [new Context360RelatedNode(7, "Fixture.Entry.Main()")],
                [],
                [],
                [],
                [],
                [])));
        memoryHandler
            .Setup(candidate => candidate.Handle(new GetNodeMemoriesQuery(42), ct))
            .ReturnsAsync(Result.Ok<MemoryNode[]>(
                [
                    new MemoryNode(
                        memoryId,
                        "Fixture.Worker.Execute()",
                        "code-hash",
                        "Preserve cancellation token.",
                        "content-hash",
                        ["async"],
                        MemoryIntent.Invariant,
                        DateTimeOffset.UtcNow,
                        false)
                ]));

        var resource = await SharpSenseMcpResources.GetNodeContext(
            contextHandler.Object,
            memoryHandler.Object,
            42,
            ct);

        resource.Uri.Should().Be("sharpsense://node/42/context");
        resource.MimeType.Should().Be("text/plain");
        resource.Text.Should().Contain("Fixture.Worker.Execute()")
            .And.Contain("Fixture.Entry.Main() (Id:7)")
            .And.Contain(memoryId.ToString());
    }

    [Fact]
    public async Task WhenNodeContextResourceTargetIsMissing_ThenItThrowsResourceNotFoundProtocolException()
    {
        var ct = TestContext.Current.CancellationToken;
        var contextHandler = new Mock<IQueryHandler<GetNodeContextQuery, Result<Context360Result>>>(MockBehavior.Strict);
        var memoryHandler = new Mock<IQueryHandler<GetNodeMemoriesQuery, Result<MemoryNode[]>>>(MockBehavior.Strict);
        contextHandler
            .Setup(candidate => candidate.Handle(new GetNodeContextQuery(999, 10), ct))
            .ReturnsAsync(Result.Fail<Context360Result>(new ServiceError(
                ServiceErrorCode.NotFound,
                "No persisted node exists for id 999.")));

        var act = async () => await SharpSenseMcpResources.GetNodeContext(
            contextHandler.Object,
            memoryHandler.Object,
            999,
            ct);

        var exception = await act.Should().ThrowExactlyAsync<McpProtocolException>();
        exception.Which.ErrorCode.Should().Be(McpErrorCode.ResourceNotFound);
        memoryHandler.VerifyNoOtherCalls();
    }

    [Fact]
    public void WhenPromptsAreConstructed_ThenTheyIncludeTargetAndPreserveLiteralBracesInOptionalArguments()
    {
        var explain = SharpSenseMcpPrompts.ExplainSymbol("HybridSearcher.Search", "How are memories ranked?");
        var impact = SharpSenseMcpPrompts.ImpactReview("IMemoryRepository", "Preserve {target} placeholder formatting");
        var audit = SharpSenseMcpPrompts.MemoryAudit("sqlite");

        explain.Role.Should().Be(Role.User);
        explain.Content.Should().BeOfType<TextContentBlock>().Which.Text
            .Should().Contain("HybridSearcher.Search")
            .And.Contain("How are memories ranked?");

        impact.Role.Should().Be(Role.User);
        impact.Content.Should().BeOfType<TextContentBlock>().Which.Text
            .Should().Contain("IMemoryRepository")
            .And.Contain("Preserve {target} placeholder formatting");

        audit.Role.Should().Be(Role.User);
        audit.Content.Should().BeOfType<TextContentBlock>().Which.Text
            .Should().Contain("Audit scope: sqlite")
            .And.Contain("sharpsense://workspace/memories/stale");
    }
}
