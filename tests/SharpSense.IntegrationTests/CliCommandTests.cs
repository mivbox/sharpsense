using AwesomeAssertions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Memory;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Testkit;
using Spectre.Console;
using Spectre.Console.Testing;
using System.Globalization;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using PersistedDocumentKind = SharpSense.Infrastructure.Persistence.Records.DocumentKind;

namespace SharpSense.IntegrationTests;

public sealed class CliCommandTests
{
    private const string RepositoryRoot = "/repo";

    [Fact]
    public async Task WhenTraceCallerDirectionRuns_ThenOutputsUpstreamNodesAsJson()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--direction", "caller", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);

        using var jsonDocument = JsonDocument.Parse(console.Output);
        var nodes = jsonDocument.RootElement;
        nodes.ValueKind.Should().Be(JsonValueKind.Array);
        nodes.GetArrayLength().Should().Be(1);

        var node = nodes[0];
        node.GetProperty("id")
            .GetInt32().Should().Be(CliCommandTestDatabase.CallerNodeId);
        node.GetProperty("canonicalId")
            .GetString().Should().Be(CliCommandTestDatabase.CallerCanonicalId);
        node.GetProperty("fullyQualifiedName")
            .GetString().Should().Be("Fixture.App.HttpEndpoint.Handle()");
        node.GetProperty("displayName")
            .GetString().Should().Be("HttpEndpoint.Handle()");
        node.GetProperty("startLine")
            .GetInt32().Should().Be(5);
        node.GetProperty("endLine")
            .GetInt32().Should().Be(12);
    }

    [Fact]
    public async Task WhenTraceCalleeDirectionRunsWithToon_ThenOutputsDownstreamNodesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--direction", "callee", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            $"- [M] `{CliCommandTestDatabase.SeedNodeId}` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28" + Environment.NewLine +
            $"  -> [M] `{CliCommandTestDatabase.CalleeNodeId}` MessageProvider.GetMessage @ src/Fixture.App/MessageProvider.cs:L7-11");
    }

    [Fact]
    public async Task WhenTraceRunsWithoutDirection_ThenItUsesCalleeTraversalByDefault()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            $"- [M] `{CliCommandTestDatabase.SeedNodeId}` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28" + Environment.NewLine +
            $"  -> [M] `{CliCommandTestDatabase.CalleeNodeId}` MessageProvider.GetMessage @ src/Fixture.App/MessageProvider.cs:L7-11");
    }

    [Fact]
    public async Task WhenTraceCallerDirectionRunsWithToon_ThenOutputsUpstreamChainToTarget()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--direction", "caller", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            $"- [M] `{CliCommandTestDatabase.CallerNodeId}` HttpEndpoint.Handle @ src/Fixture.App/HttpEndpoint.cs:L5-12" + Environment.NewLine +
            $"  -> [M] `{CliCommandTestDatabase.SeedNodeId}` MessageConsumer.Render @ src/Fixture.App/MessageConsumer.cs:L20-28");
    }

    [Fact]
    public async Task WhenSearchRunsWithToon_ThenOutputsMatchingNodesInHierarchicalToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["search", "MessageProvider", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            "src/Fixture.App/:" + Environment.NewLine +
            "  MessageProvider.cs:" + Environment.NewLine +
            $"    - [M] `{CliCommandTestDatabase.CalleeNodeId}` MessageProvider.GetMessage L7-11");
    }

    [Fact]
    public async Task WhenSearchRunsWithToonForDocumentNode_ThenOutputsDocumentNodesInHierarchicalToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["search", "Getting Started", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            "docs/:" + Environment.NewLine +
            "  Guide.md:" + Environment.NewLine +
            $"    - [D] `{CliCommandTestDatabase.DocumentNodeId}` Guide#getting-started L1-3");
    }

    [Fact]
    public async Task WhenContextNodeIsMissing_ThenItWritesToStderrAndReturnsANonzeroExitCode()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        using var errors = new StringWriter();
        var previousConsole = AnsiConsole.Console;
        var previousError = Console.Error;

        try
        {
            AnsiConsole.Console = console;
            Console.SetError(errors);
            var app = CreateCommandApp(null, database);

            var exitCode = await app.RunAsync(
                ["context", "--node-id", "999999", "--include-memories", "--repo-root", RepositoryRoot],
                TestContext.Current.CancellationToken);

            exitCode.Should().Be(1);
            console.Output.Should().BeEmpty();
            errors.ToString().Should().Be($"Error: No persisted node exists for id 999999.{Environment.NewLine}");
        }
        finally
        {
            AnsiConsole.Console = previousConsole;
            Console.SetError(previousError);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenContextRunsWithoutToon_ThenItOutputsNodeRelationshipsAsJson(bool includeMemories)
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);
        var memoryOptions = includeMemories ? new[] { "--include-memories" } : [];

        var exitCode = await app.RunAsync(
            ["context", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--repo-root", RepositoryRoot, .. memoryOptions],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        using var json = JsonDocument.Parse(console.Output);
        var context = json.RootElement;
        var target = context.GetProperty("targetNode");
        target.GetProperty("id")
            .GetInt32().Should().Be(CliCommandTestDatabase.SeedNodeId);
        target.GetProperty("name")
            .GetString().Should().Be("MessageConsumer.Render()");
        target.GetProperty("kind")
            .GetString().Should().Be("Method");
        var caller = context.GetProperty("callers")
            .EnumerateArray().Should().ContainSingle().Which;
        caller.GetProperty("id")
            .GetInt32().Should().Be(CliCommandTestDatabase.CallerNodeId);
        var callee = context.GetProperty("callees")
            .EnumerateArray().Should().ContainSingle().Which;
        callee.GetProperty("id")
            .GetInt32().Should().Be(CliCommandTestDatabase.CalleeNodeId);
        foreach (var relationship in new[] { "implementers", "inherits", "parents", "children" })
        {
            context.GetProperty(relationship)
                .EnumerateArray().Should().BeEmpty();
        }

        context.TryGetProperty("memories", out var memories).Should().Be(includeMemories);
        if (includeMemories)
        {
            memories.EnumerateArray().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task WhenContextRunsForMethodNode_ThenItOutputsImmediateCallersAndCalleesAsToon()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["context", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--repo-root", RepositoryRoot, "--toon"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            "node:" + Environment.NewLine +
            $"  id: {CliCommandTestDatabase.SeedNodeId}" + Environment.NewLine +
            "  name: MessageConsumer.Render()" + Environment.NewLine +
            "  kind: M" + Environment.NewLine +
            "  file: src/Fixture.App/MessageConsumer.cs:20-28" + Environment.NewLine +
            Environment.NewLine +
            "incoming:" + Environment.NewLine +
            $"  callers: [HttpEndpoint.Handle (Id:{CliCommandTestDatabase.CallerNodeId})]" + Environment.NewLine +
            "  implementers: []" + Environment.NewLine +
            Environment.NewLine +
            "outgoing:" + Environment.NewLine +
            $"  callees: [MessageProvider.GetMessage (Id:{CliCommandTestDatabase.CalleeNodeId})]" + Environment.NewLine +
            "  inherits: []" + Environment.NewLine +
            Environment.NewLine +
            "structural:" + Environment.NewLine +
            "  parents: []" + Environment.NewLine +
            "  children: []");
    }

    [Fact]
    public async Task WhenContextRunsForInterfaceNode_ThenItOutputsIncomingImplementersAsToon()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["context", "--node-id", CliCommandTestDatabase.InterfaceNodeId.ToString(CultureInfo.InvariantCulture), "--repo-root", RepositoryRoot, "--toon"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            "node:" + Environment.NewLine +
            $"  id: {CliCommandTestDatabase.InterfaceNodeId}" + Environment.NewLine +
            "  name: IMessageRenderer" + Environment.NewLine +
            "  kind: I" + Environment.NewLine +
            "  file: src/Fixture.App/IMessageRenderer.cs:3-8" + Environment.NewLine +
            Environment.NewLine +
            "incoming:" + Environment.NewLine +
            "  callers: []" + Environment.NewLine +
            $"  implementers: [HtmlRenderer (Id:{CliCommandTestDatabase.HtmlRendererNodeId}), TerminalRenderer (Id:{CliCommandTestDatabase.TerminalRendererNodeId})]" + Environment.NewLine +
            Environment.NewLine +
            "outgoing:" + Environment.NewLine +
            "  callees: []" + Environment.NewLine +
            "  inherits: []" + Environment.NewLine +
            Environment.NewLine +
            "structural:" + Environment.NewLine +
            "  parents: []" + Environment.NewLine +
            "  children: []");
    }

    [Fact]
    public async Task WhenContextRunsForDerivedClassNode_ThenItOutputsOutgoingInheritanceAsToon()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["context", "--node-id", CliCommandTestDatabase.DerivedClassNodeId.ToString(CultureInfo.InvariantCulture), "--repo-root", RepositoryRoot, "--toon"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            "node:" + Environment.NewLine +
            $"  id: {CliCommandTestDatabase.DerivedClassNodeId}" + Environment.NewLine +
            "  name: FancyRenderer" + Environment.NewLine +
            "  kind: C" + Environment.NewLine +
            "  file: src/Fixture.App/FancyRenderer.cs:3-18" + Environment.NewLine +
            Environment.NewLine +
            "incoming:" + Environment.NewLine +
            "  callers: []" + Environment.NewLine +
            "  implementers: []" + Environment.NewLine +
            Environment.NewLine +
            "outgoing:" + Environment.NewLine +
            "  callees: []" + Environment.NewLine +
            $"  inherits: [BaseRenderer (Id:{CliCommandTestDatabase.BaseClassNodeId})]" + Environment.NewLine +
            Environment.NewLine +
            "structural:" + Environment.NewLine +
            "  parents: []" + Environment.NewLine +
            "  children: []");
    }

    [Fact]
    public async Task WhenTraceCalleeDirectionRunsForDocumentRoot_ThenOutputsDownstreamDocumentNodesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["trace", CliCommandTestDatabase.DocumentRootNodeId.ToString(CultureInfo.InvariantCulture), "--direction", "callee", "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            $"- [D] `{CliCommandTestDatabase.DocumentRootNodeId}` DocA @ docs/DocA.md:L1" + Environment.NewLine +
            $"  -> [D] `{CliCommandTestDatabase.LinkedDocumentRootNodeId}` Reference @ docs/Reference.md:L1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("/exported-skills")]
    public async Task WhenLegacySkillsCommandRuns_ThenItFailsWithoutWritingFiles(string? destination)
    {
        using var console = new TestConsole();
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            RepositoryRoot);
        var app = Cli.Program.CreateCommandApp(
            console,
            services => services.AddSingleton<IFileSystem>(fileSystem),
            enableFileLogging: false);

        var exitCode = await app.RunAsync(
            destination is null ? ["skills"] : ["skills", destination],
            TestContext.Current.CancellationToken);

        exitCode.Should().NotBe(0);
        console.Output.Should().Contain("skills");
        fileSystem.AllFiles.Should().BeEquivalentTo(new[]
        {
            "/repo/.git/HEAD"
        });
        fileSystem.Directory.Exists("/repo/.agents").Should().BeFalse();
        fileSystem.Directory.Exists("/exported-skills").Should().BeFalse();
    }

    [Fact]
    public async Task WhenHelpRuns_ThenItDoesNotAdvertiseLegacySkillsInstallation()
    {
        using var console = new TestConsole();
        var app = Cli.Program.CreateCommandApp(console, enableFileLogging: false);

        var exitCode = await app.RunAsync(
            ["--help"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("workspace");
        console.Output.Should().Contain("ui");
        console.Output.Should().NotContain("skills");
    }

    [Fact]
    public async Task WhenInheritorsRunsForClassNodeWithToon_ThenOutputsDerivedClassesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["inheritors", CliCommandTestDatabase.BaseClassNodeId.ToString(CultureInfo.InvariantCulture), "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be($"[C] `{CliCommandTestDatabase.DerivedClassNodeId}` FancyRenderer @ src/Fixture.App/FancyRenderer.cs:3-18");
    }

    [Fact]
    public async Task WhenInheritorsRunsForInterfaceNodeWithToon_ThenOutputsImplementingClassesInToonFormat()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["inheritors", CliCommandTestDatabase.InterfaceNodeId.ToString(CultureInfo.InvariantCulture), "--toon", "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            $"[C] `{CliCommandTestDatabase.HtmlRendererNodeId}` HtmlRenderer @ src/Fixture.App/HtmlRenderer.cs:3-16" +
            Environment.NewLine +
            $"[C] `{CliCommandTestDatabase.TerminalRendererNodeId}` TerminalRenderer @ src/Fixture.App/TerminalRenderer.cs:3-15");
    }

    [Fact]
    public async Task WhenExecuteRunsWithToon_ThenItFormatsCondensedCommandOutput()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var ct = TestContext.Current.CancellationToken;
        CancellationToken executionToken = default;
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory.Setup(candidate => candidate.Create(It.Is<CancellationToken>(token => token.CanBeCanceled)))
            .Callback<CancellationToken>(token => executionToken = token)
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex.Setup(candidate => candidate.AppendLine("Build succeeded in 13.7s", It.Is<CancellationToken>(token => token == executionToken)))
            .ReturnsAsync(Result.Ok(1));
        executeLogIndex.Setup(candidate => candidate.FindMatches("Build succeeded", It.Is<CancellationToken>(token => token == executionToken)))
            .ReturnsAsync(Result.Ok<int[]>([1]));
        executeLogIndex.Setup(candidate => candidate.ReadRange(new ExecutionLineRange(1, 1), It.Is<CancellationToken>(token => token == executionToken)))
            .ReturnsAsync(Result.Ok<ExecutionLogLine[]>(
            [
                new ExecutionLogLine(1, "Build succeeded in 13.7s")
            ]));
        executeLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
            It.Is<CommandProcessRequest>(request =>
                    request.Command == "dotnet build SharpSense.sln" &&
                    request.WorkingDirectory == RepositoryRoot),
            It.IsAny<Func<string, CancellationToken, Task>>(),
            It.Is<CancellationToken>(token => token == executionToken)))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                await onOutput("Build succeeded in 13.7s", innerCt);

                return Result.Ok(new CommandProcessResult(0, 1));
            });
        var app = CreateCommandApp(
            console,
            database,
            configureServices: services =>
            {
                services.RemoveAll<ICommandProcessRunner>();
                services.RemoveAll<IExecuteLogIndexFactory>();
                services.AddScoped<ICommandProcessRunner>(_ => processRunner.Object);
                services.AddScoped<IExecuteLogIndexFactory>(_ => executeLogIndexFactory.Object);
            });

        var exitCode = await app.RunAsync(
            ["execute", "dotnet build SharpSense.sln", "--query", "Build succeeded", "--toon", "--repo-root", RepositoryRoot],
            ct);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            "command: dotnet build SharpSense.sln" + Environment.NewLine +
            "status: success" + Environment.NewLine +
            "exit_code: 0" + Environment.NewLine +
            "working_directory: /repo" + Environment.NewLine +
            "query: Build succeeded" + Environment.NewLine +
            "metrics:" + Environment.NewLine +
            "  captured_lines: 1" + Environment.NewLine +
            "  matched_lines: 1" + Environment.NewLine +
            "  block_count: 1" + Environment.NewLine +
            "  truncated: false" + Environment.NewLine +
            "summary: Returned 1 merged block(s) from 1 matched line(s) across 1 captured line(s)." + Environment.NewLine +
            "output:" + Environment.NewLine +
            "  - span: 1-1" + Environment.NewLine +
            "    text: |" + Environment.NewLine +
            "      1| Build succeeded in 13.7s");
    }

    [Fact]
    public async Task WhenExecuteFails_ThenItOutputsErrorJsonAndReturnsExitCodeOne()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var ct = TestContext.Current.CancellationToken;
        CancellationToken executionToken = default;
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory.Setup(candidate => candidate.Create(It.Is<CancellationToken>(token => token.CanBeCanceled)))
            .Callback<CancellationToken>(token => executionToken = token)
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
            It.Is<CommandProcessRequest>(request =>
                    request.Command == "missing-command" &&
                    request.WorkingDirectory == RepositoryRoot),
            It.IsAny<Func<string, CancellationToken, Task>>(),
            It.Is<CancellationToken>(token => token == executionToken)))
            .ReturnsAsync(Result.Fail<CommandProcessResult>("Failed to start command 'missing-command'."));
        var app = CreateCommandApp(
            console,
            database,
            configureServices: services =>
            {
                services.RemoveAll<ICommandProcessRunner>();
                services.RemoveAll<IExecuteLogIndexFactory>();
                services.AddScoped<ICommandProcessRunner>(_ => processRunner.Object);
                services.AddScoped<IExecuteLogIndexFactory>(_ => executeLogIndexFactory.Object);
            });

        var exitCode = await app.RunAsync(
            ["execute", "missing-command", "--query", "Error", "--repo-root", RepositoryRoot],
            ct);

        exitCode.Should().Be(1);

        using var jsonDocument = JsonDocument.Parse(console.Output);
        jsonDocument.RootElement.GetProperty("command")
            .GetString().Should().Be("missing-command");
        jsonDocument.RootElement.GetProperty("status")
            .GetString().Should().Be("error");
        jsonDocument.RootElement.GetProperty("errorMessage")
            .GetString().Should().Be("Failed to start command 'missing-command'.");
    }

    private static Spectre.Console.Cli.CommandApp CreateCommandApp(
        TestConsole? console,
        CliCommandTestDatabase database,
        MockFileSystem? fileSystem = null,
        Action<IServiceCollection>? configureServices = null)
    {
        fileSystem ??= new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            RepositoryRoot);

        return Cli.Program.CreateCommandApp(
            console,
            services =>
            {
                services.AddWorkspaceFixture(RepositoryRoot, fileSystem);
                database.ConfigureServices(services);
                configureServices?.Invoke(services);
            },
            enableFileLogging: false);
    }

    [Fact]
    public async Task WhenMemoryAddRuns_ThenItAttachesAndReturnsTheNewMemory()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);
        var ct = TestContext.Current.CancellationToken;

        var exitCode = await app.RunAsync(
            ["memory", "add", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--content", "Always greet politely", "--tag", "convention", "--repo-root", RepositoryRoot],
            ct);

        var actualOutput = console.Output;
        exitCode.Should().Be(0, "actual output was: <" + actualOutput + ">");
        actualOutput.Should().Contain("added_memory: true");
        actualOutput.Should().Contain("memory_id: ");
        actualOutput.Should().Contain("content: \"Always greet politely\"");

        await using var verification = await database.GetDbContext();
        var memoryCount = await verification.MemoryNodes.CountAsync(ct);
        memoryCount.Should().Be(1, "the memory must be persisted by the CLI command");
    }

    [Fact]
    public async Task WhenMemoryListRuns_ThenItPrintsAttachedMemories()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var seedConsole = new TestConsole();
        var seedApp = CreateCommandApp(seedConsole, database);
        var ct = TestContext.Current.CancellationToken;

        var addExit = await seedApp.RunAsync(
            ["memory", "add", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--content", "Always greet politely", "--repo-root", RepositoryRoot],
            ct);
        addExit.Should().Be(0);

        using var listConsole = new TestConsole();
        var listApp = CreateCommandApp(listConsole, database);

        var exitCode = await listApp.RunAsync(
            ["memory", "list", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--repo-root", RepositoryRoot],
            ct);

        exitCode.Should().Be(0);
        listConsole.Output.Should().Contain("count: 1");
        listConsole.Output.Should().Contain("content: \"Always greet politely\"");
    }

    [Fact]
    public async Task WhenMemoryRemoveRuns_ThenItDeletesTheMemory()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var seedConsole = new TestConsole();
        var seedApp = CreateCommandApp(seedConsole, database);
        var ct = TestContext.Current.CancellationToken;

        var addExit = await seedApp.RunAsync(
            ["memory", "add", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--content", "Always greet politely", "--repo-root", RepositoryRoot],
            ct);
        addExit.Should().Be(0);
        var memoryId = await GetSingleMemoryId(database, ct);

        using var removeConsole = new TestConsole();
        var removeApp = CreateCommandApp(removeConsole, database);

        var exitCode = await removeApp.RunAsync(
            ["memory", "remove", "--memory-id", memoryId.ToString(), "--repo-root", RepositoryRoot],
            ct);

        exitCode.Should().Be(0);
        removeConsole.Output.Should().Contain($"removed_memory: {memoryId}");

        await using var verification = await database.GetDbContext();
        var memoryCount = await verification.MemoryNodes.CountAsync(ct);
        memoryCount.Should().Be(0, "the memory must be removed by the CLI command");
    }

    [Fact]
    public async Task WhenMemoryRemoveRunsWithUnknownId_ThenItReturnsExitCodeOne()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);
        var unknownId = Guid.NewGuid();

        var exitCode = await app.RunAsync(
            ["memory", "remove", "--memory-id", unknownId.ToString(), "--repo-root", RepositoryRoot],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("remove failed:");
    }

    [Fact]
    public async Task WhenMemoryGetRuns_ThenItPrintsFullMemoryContent()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var seedConsole = new TestConsole();
        var seedApp = CreateCommandApp(seedConsole, database);
        var ct = TestContext.Current.CancellationToken;

        var addExit = await seedApp.RunAsync(
            ["memory", "add", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--content", "Always greet politely", "--tag", "convention", "--repo-root", RepositoryRoot],
            ct);
        addExit.Should().Be(0);
        var memoryId = await GetSingleMemoryId(database, ct);

        using var getConsole = new TestConsole();
        var getApp = CreateCommandApp(getConsole, database);

        var exitCode = await getApp.RunAsync(
            ["memory", "get", "--memory-id", memoryId.ToString(), "--repo-root", RepositoryRoot],
            ct);

        exitCode.Should().Be(0);
        getConsole.Output.Should().Contain("memory:");
        getConsole.Output.Should().Contain($"id: {memoryId}");
        getConsole.Output.Should().Contain("Always greet politely");
        getConsole.Output.Should().Contain("tags: [convention]");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenContextRunsWithIncludeMemories_ThenItPrintsOnlyMemoryMetadata(bool useToon)
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var seedConsole = new TestConsole();
        var seedApp = CreateCommandApp(seedConsole, database);
        var ct = TestContext.Current.CancellationToken;

        var addExitCode = await seedApp.RunAsync(
            ["memory", "add", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--content", "Always greet politely", "--tag", "convention", "--repo-root", RepositoryRoot],
            ct);
        addExitCode.Should().Be(0);
        var memoryId = await GetSingleMemoryId(database, ct);

        using var contextConsole = new TestConsole();
        var contextApp = CreateCommandApp(contextConsole, database);
        var outputOptions = useToon ? new[] { "--toon" } : [];

        var exitCode = await contextApp.RunAsync(
            ["context", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--include-memories", "--repo-root", RepositoryRoot, .. outputOptions],
            ct);

        exitCode.Should().Be(0);
        contextConsole.Output.Should().NotContain("Always greet politely");
        if (useToon)
        {
            contextConsole.Output.Should().Contain("memories: 1 (");
            contextConsole.Output.Should().Contain("semantic_context:");
            contextConsole.Output.Should().Contain($"id={memoryId}");
            contextConsole.Output.Should().Contain("stale=false");
        }
        else
        {
            using var json = JsonDocument.Parse(contextConsole.Output);
            var memory = json.RootElement.GetProperty("memories")
                .EnumerateArray().Should().ContainSingle().Which;
            memory.GetProperty("id")
                .GetGuid().Should().Be(memoryId);
            memory.GetProperty("intent")
                .GetString().Should().Be("Convention");
            memory.GetProperty("isStale")
                .GetBoolean().Should().BeFalse();
            memory.GetProperty("tags")
                .EnumerateArray()
                .Select(tag => tag.GetString()).Should().Equal("convention");
            memory.TryGetProperty("content", out _).Should().BeFalse();
        }
    }

    private static async Task<Guid> GetSingleMemoryId(CliCommandTestDatabase database, CancellationToken ct)
    {
        await using var context = await database.GetDbContext();

        return await context.MemoryNodes.Select(static memory => memory.Id)
            .SingleAsync(ct);
    }

    private sealed class NoopEmbeddingGenerator : IEmbeddingGenerator
    {
        public Task<TextEmbedding> Generate(string text, CancellationToken ct = default)
            => Task.FromResult(new TextEmbedding(text, Array.Empty<float>()));

        public Task<IReadOnlyList<TextEmbedding>> GenerateBatch(
            IEnumerable<string> texts,
            IProgress<EmbeddingGenerationProgress>? progress,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TextEmbedding>>(
                texts.Select(text => new TextEmbedding(text, Array.Empty<float>()))
                    .ToArray());
    }

    private sealed class CliCommandTestDatabase(InMemoryContextFactory<SharpSenseDbContext> contextFactory) : IAsyncDisposable
    {
        public const string ProjectId = "project-app";
        public const int SeedNodeId = 1;
        public const int CallerNodeId = 2;
        public const int CalleeNodeId = 3;
        public const int DocumentRootNodeId = 4;
        public const int LinkedDocumentRootNodeId = 5;
        public const int DocumentNodeId = 6;
        public const int InterfaceNodeId = 7;
        public const int HtmlRendererNodeId = 8;
        public const int TerminalRendererNodeId = 9;
        public const int BaseClassNodeId = 10;
        public const int DerivedClassNodeId = 11;
        public const string SeedCanonicalId = "code:project-app:Fixture.App.MessageConsumer.Render()";
        public const string CallerCanonicalId = "code:project-app:Fixture.App.HttpEndpoint.Handle()";
        public const string CalleeCanonicalId = "code:project-app:Fixture.App.MessageProvider.GetMessage()";
        public const string DocumentRootCanonicalId = "code:doc:docs/DocA.md#document-root";
        public const string LinkedDocumentRootCanonicalId = "code:doc:docs/Reference.md#document-root";
        public const string DocumentNodeCanonicalId = "code:doc:docs/Guide.md#getting-started";
        public const string InterfaceCanonicalId = "code:project-app:Fixture.App.IMessageRenderer";
        public const string HtmlRendererCanonicalId = "code:project-app:Fixture.App.HtmlRenderer";
        public const string TerminalRendererCanonicalId = "code:project-app:Fixture.App.TerminalRenderer";
        public const string BaseClassCanonicalId = "code:project-app:Fixture.App.BaseRenderer";
        public const string DerivedClassCanonicalId = "code:project-app:Fixture.App.FancyRenderer";

        public static async Task<CliCommandTestDatabase> Create()
        {
            var contextFactory = new InMemoryContextFactory<SharpSenseDbContext>(
                options => new SharpSenseDbContext(options),
                new InMemoryContextFactoryOptions(
                    UseMigrations: true,
                    LoadVectorExtension: true));
            var database = new CliCommandTestDatabase(contextFactory);
            await database.Initialize();

            return database;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IHostedService>();
            contextFactory.ConfigureServices(services);
            services.AddMemory();
            services.AddMemoryInfrastructure();
            services.TryAddSingleton<IEmbeddingGenerator, NoopEmbeddingGenerator>();
        }

        public async Task<SharpSenseDbContext> GetDbContext()
            => await contextFactory.GetContext(ct: TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await contextFactory.DisposeAsync();
        }

        private async Task Initialize()
        {
            await using var dbContext = await contextFactory.GetContext(
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
                    Path = "src/Fixture.App",
                    Name = "Fixture.App"
                },
                new DirectoryRecord
                {
                    Id = 4,
                    ParentId = 1,
                    Path = "docs",
                    Name = "docs"
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
                    Depth = 1
                },
                new DirectoryClosureRecord
                {
                    AncestorDirectoryId = 2,
                    DescendantDirectoryId = 3,
                    Depth = 1
                });
            dbContext.Documents.AddRange(
                new DocumentRecord
                {
                    Id = 10,
                    DirectoryId = 3,
                    FileName = "Fixture.App.csproj",
                    Extension = ".csproj",
                    RelativePath = "src/Fixture.App/Fixture.App.csproj",
                    Kind = PersistedDocumentKind.ProjectFile
                },
                new DocumentRecord
                {
                    Id = 11,
                    DirectoryId = 3,
                    FileName = "MessageConsumer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/MessageConsumer.cs",
                    Kind = PersistedDocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 12,
                    DirectoryId = 3,
                    FileName = "HttpEndpoint.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/HttpEndpoint.cs",
                    Kind = PersistedDocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 13,
                    DirectoryId = 3,
                    FileName = "MessageProvider.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/MessageProvider.cs",
                    Kind = PersistedDocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 14,
                    DirectoryId = 4,
                    FileName = "DocA.md",
                    Extension = ".md",
                    RelativePath = "docs/DocA.md",
                    Kind = PersistedDocumentKind.Markdown
                },
                new DocumentRecord
                {
                    Id = 15,
                    DirectoryId = 4,
                    FileName = "Reference.md",
                    Extension = ".md",
                    RelativePath = "docs/Reference.md",
                    Kind = PersistedDocumentKind.Markdown
                },
                new DocumentRecord
                {
                    Id = 16,
                    DirectoryId = 4,
                    FileName = "Guide.md",
                    Extension = ".md",
                    RelativePath = "docs/Guide.md",
                    Kind = PersistedDocumentKind.Markdown
                },
                new DocumentRecord
                {
                    Id = 17,
                    DirectoryId = 3,
                    FileName = "IMessageRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/IMessageRenderer.cs",
                    Kind = PersistedDocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 18,
                    DirectoryId = 3,
                    FileName = "HtmlRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/HtmlRenderer.cs",
                    Kind = PersistedDocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 19,
                    DirectoryId = 3,
                    FileName = "TerminalRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/TerminalRenderer.cs",
                    Kind = PersistedDocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 20,
                    DirectoryId = 3,
                    FileName = "BaseRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/BaseRenderer.cs",
                    Kind = PersistedDocumentKind.Source
                },
                new DocumentRecord
                {
                    Id = 21,
                    DirectoryId = 3,
                    FileName = "FancyRenderer.cs",
                    Extension = ".cs",
                    RelativePath = "src/Fixture.App/FancyRenderer.cs",
                    Kind = PersistedDocumentKind.Source
                });
            dbContext.GraphNodes.AddRange(
                new GraphNodeRecord
                {
                    Id = 100,
                    CanonicalId = ProjectId,
                    Kind = GraphNodeKind.Project
                },
                new GraphNodeRecord
                {
                    Id = SeedNodeId,
                    CanonicalId = SeedCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = CallerNodeId,
                    CanonicalId = CallerCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = CalleeNodeId,
                    CanonicalId = CalleeCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = DocumentRootNodeId,
                    CanonicalId = DocumentRootCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = LinkedDocumentRootNodeId,
                    CanonicalId = LinkedDocumentRootCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = DocumentNodeId,
                    CanonicalId = DocumentNodeCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = InterfaceNodeId,
                    CanonicalId = InterfaceCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = HtmlRendererNodeId,
                    CanonicalId = HtmlRendererCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = TerminalRendererNodeId,
                    CanonicalId = TerminalRendererCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = BaseClassNodeId,
                    CanonicalId = BaseClassCanonicalId,
                    Kind = GraphNodeKind.Code
                },
                new GraphNodeRecord
                {
                    Id = DerivedClassNodeId,
                    CanonicalId = DerivedClassCanonicalId,
                    Kind = GraphNodeKind.Code
                });
            dbContext.ProjectNodes.Add(
                new ProjectNodeRecord
                {
                    Id = 100,
                    Name = "Fixture.App",
                    ProjectDocumentId = 10,
                    ContentHash = "fixture-app"
                });
            dbContext.CodeNodes.AddRange(
                new CodeNodeRecord
                {
                    Id = SeedNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 11,
                    FullyQualifiedName = "Fixture.App.MessageConsumer.Render()",
                    DisplayName = "MessageConsumer.Render()",
                    NodeType = NodeType.Method,
                    StartLine = 20,
                    EndLine = 28,
                    Summary = "Renders the message.",
                    SearchText = "MessageConsumer.Render()\nRenders the message."
                },
                new CodeNodeRecord
                {
                    Id = CallerNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 12,
                    FullyQualifiedName = "Fixture.App.HttpEndpoint.Handle()",
                    DisplayName = "HttpEndpoint.Handle()",
                    NodeType = NodeType.Method,
                    StartLine = 5,
                    EndLine = 12,
                    Summary = "Handles the HTTP endpoint.",
                    SearchText = "HttpEndpoint.Handle()\nHandles the HTTP endpoint."
                },
                new CodeNodeRecord
                {
                    Id = CalleeNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 13,
                    FullyQualifiedName = "Fixture.App.MessageProvider.GetMessage()",
                    DisplayName = "MessageProvider.GetMessage()",
                    NodeType = NodeType.Method,
                    StartLine = 7,
                    EndLine = 11,
                    Summary = "Gets a message.",
                    SearchText = "MessageProvider.GetMessage()\nGets a message."
                },
                new CodeNodeRecord
                {
                    Id = DocumentRootNodeId,
                    ProjectNodeId = null,
                    DocumentId = 14,
                    FullyQualifiedName = "docs/DocA.md#document-root",
                    DisplayName = "DocA",
                    NodeType = NodeType.Document,
                    StartLine = 1,
                    EndLine = 1,
                    Summary = "See [Reference](./Reference.md).",
                    SearchText = "DocA\nSee [Reference](./Reference.md)."
                },
                new CodeNodeRecord
                {
                    Id = LinkedDocumentRootNodeId,
                    ProjectNodeId = null,
                    DocumentId = 15,
                    FullyQualifiedName = "docs/Reference.md#document-root",
                    DisplayName = "Reference",
                    NodeType = NodeType.Document,
                    StartLine = 1,
                    EndLine = 1,
                    Summary = "Reference document.",
                    SearchText = "Reference\nReference document."
                },
                new CodeNodeRecord
                {
                    Id = DocumentNodeId,
                    ProjectNodeId = null,
                    DocumentId = 16,
                    FullyQualifiedName = "docs/Guide.md#getting-started",
                    DisplayName = "Guide#getting-started",
                    NodeType = NodeType.Document,
                    StartLine = 1,
                    EndLine = 3,
                    Summary = "Getting Started guide.",
                    SearchText = "Guide#getting-started\nGetting Started guide."
                },
                new CodeNodeRecord
                {
                    Id = InterfaceNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 17,
                    FullyQualifiedName = "Fixture.App.IMessageRenderer",
                    DisplayName = "IMessageRenderer",
                    NodeType = NodeType.Interface,
                    StartLine = 3,
                    EndLine = 8,
                    Summary = "Renderer contract.",
                    SearchText = "IMessageRenderer\nRenderer contract."
                },
                new CodeNodeRecord
                {
                    Id = HtmlRendererNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 18,
                    FullyQualifiedName = "Fixture.App.HtmlRenderer",
                    DisplayName = "HtmlRenderer",
                    NodeType = NodeType.Class,
                    StartLine = 3,
                    EndLine = 16,
                    Summary = "HTML renderer.",
                    SearchText = "HtmlRenderer\nHTML renderer."
                },
                new CodeNodeRecord
                {
                    Id = TerminalRendererNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 19,
                    FullyQualifiedName = "Fixture.App.TerminalRenderer",
                    DisplayName = "TerminalRenderer",
                    NodeType = NodeType.Class,
                    StartLine = 3,
                    EndLine = 15,
                    Summary = "Terminal renderer.",
                    SearchText = "TerminalRenderer\nTerminal renderer."
                },
                new CodeNodeRecord
                {
                    Id = BaseClassNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 20,
                    FullyQualifiedName = "Fixture.App.BaseRenderer",
                    DisplayName = "BaseRenderer",
                    NodeType = NodeType.Class,
                    StartLine = 3,
                    EndLine = 14,
                    Summary = "Base renderer.",
                    SearchText = "BaseRenderer\nBase renderer."
                },
                new CodeNodeRecord
                {
                    Id = DerivedClassNodeId,
                    ProjectNodeId = 100,
                    DocumentId = 21,
                    FullyQualifiedName = "Fixture.App.FancyRenderer",
                    DisplayName = "FancyRenderer",
                    NodeType = NodeType.Class,
                    StartLine = 3,
                    EndLine = 18,
                    Summary = "Fancy renderer.",
                    SearchText = "FancyRenderer\nFancy renderer."
                });

            dbContext.DependencyEdges.AddRange(
                new DependencyEdgeRecord
                {
                    CallerNodeId = CallerNodeId,
                    CalleeNodeId = SeedNodeId,
                    EdgeType = EdgeType.MethodCall
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = SeedNodeId,
                    CalleeNodeId = CalleeNodeId,
                    EdgeType = EdgeType.MethodCall
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = DocumentRootNodeId,
                    CalleeNodeId = LinkedDocumentRootNodeId,
                    EdgeType = EdgeType.DocumentLink
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = HtmlRendererNodeId,
                    CalleeNodeId = InterfaceNodeId,
                    EdgeType = EdgeType.Implements
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = TerminalRendererNodeId,
                    CalleeNodeId = InterfaceNodeId,
                    EdgeType = EdgeType.Implements
                },
                new DependencyEdgeRecord
                {
                    CallerNodeId = DerivedClassNodeId,
                    CalleeNodeId = BaseClassNodeId,
                    EdgeType = EdgeType.Implements
                });

            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM CodeNodeSearch;
                INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, SearchText, RelativeFilePath)
                SELECT codeNode.Id, graphNode.CanonicalId, codeNode.DisplayName, codeNode.FullyQualifiedName, codeNode.SearchText, document.RelativePath
                FROM CodeNodes AS codeNode
                INNER JOIN GraphNodes AS graphNode ON graphNode.Id = codeNode.Id
                INNER JOIN Documents AS document ON document.Id = codeNode.DocumentId;
                """,
                TestContext.Current.CancellationToken);
        }
    }
}
