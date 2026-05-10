using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using AwesomeAssertions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Refactoring.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using SharpSense.Testkit;
using Spectre.Console.Testing;
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
        node.GetProperty("id").GetInt32().Should().Be(CliCommandTestDatabase.CallerNodeId);
        node.GetProperty("canonicalId").GetString().Should().Be(CliCommandTestDatabase.CallerCanonicalId);
        node.GetProperty("fullyQualifiedName").GetString().Should().Be("Fixture.App.HttpEndpoint.Handle()");
        node.GetProperty("displayName").GetString().Should().Be("HttpEndpoint.Handle()");
        node.GetProperty("startLine").GetInt32().Should().Be(5);
        node.GetProperty("endLine").GetInt32().Should().Be(12);
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
    public async Task WhenContextRunsForMethodNode_ThenItOutputsImmediateCallersAndCalleesAsToon()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["context", "--node-id", CliCommandTestDatabase.SeedNodeId.ToString(CultureInfo.InvariantCulture), "--repo-root", RepositoryRoot],
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
            "  inherits: []");
    }

    [Fact]
    public async Task WhenContextRunsForInterfaceNode_ThenItOutputsIncomingImplementersAsToon()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["context", "--node-id", CliCommandTestDatabase.InterfaceNodeId.ToString(CultureInfo.InvariantCulture), "--repo-root", RepositoryRoot],
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
            "  inherits: []");
    }

    [Fact]
    public async Task WhenContextRunsForDerivedClassNode_ThenItOutputsOutgoingInheritanceAsToon()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var app = CreateCommandApp(console, database);

        var exitCode = await app.RunAsync(
            ["context", "--node-id", CliCommandTestDatabase.DerivedClassNodeId.ToString(CultureInfo.InvariantCulture), "--repo-root", RepositoryRoot],
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
            $"  inherits: [BaseRenderer (Id:{CliCommandTestDatabase.BaseClassNodeId})]");
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

    [Fact]
    public async Task WhenSkillsRunsWithoutPath_ThenItInstallsEmbeddedSkillsUnderCurrentRoot()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);
        var app = CreateCommandApp(console, database, fileSystem);

        var exitCode = await app.RunAsync(
            ["skills"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        fileSystem.FileExists("/repo/.agents/skills/sharpsense/SKILL.md").Should().BeFalse();
        fileSystem.FileExists("/repo/.agents/skills/sharpsense-refactoring/SKILL.md").Should().BeTrue();
        fileSystem.FileExists("/repo/.agents/skills/sharpsense-exploring/SKILL.md").Should().BeTrue();
        fileSystem.FileExists("/repo/.agents/skills/sharpsense-impact-analysis/SKILL.md").Should().BeTrue();
        fileSystem.GetFile("/repo/.agents/skills/sharpsense-refactoring/SKILL.md").TextContents.Should().Contain("name: sharpsense-refactoring");
        fileSystem.GetFile("/repo/.agents/skills/sharpsense-exploring/SKILL.md").TextContents.Should().Contain("name: sharpsense-exploring");
        fileSystem.GetFile("/repo/.agents/skills/sharpsense-impact-analysis/SKILL.md").TextContents.Should().Contain("name: sharpsense-impact-analysis");
        console.Output.Should().Contain("/repo/.agents/skills");
        console.Output.Should().NotContain("sharpsense/SKILL.md");
        console.Output.Should().Contain("sharpsense-refactoring/SKILL.md");
        console.Output.Should().Contain("sharpsense-exploring/SKILL.md");
        console.Output.Should().Contain("sharpsense-impact-analysis/SKILL.md");
    }

    [Fact]
    public async Task WhenSkillsRunsWithCustomPath_ThenItInstallsEmbeddedSkillsUnderChosenRoot()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);
        var app = CreateCommandApp(console, database, fileSystem);

        var exitCode = await app.RunAsync(
            ["skills", "/exported-skills"],
            TestContext.Current.CancellationToken);

        exitCode.Should().Be(0);
        fileSystem.FileExists("/exported-skills/.agents/skills/sharpsense/SKILL.md").Should().BeFalse();
        fileSystem.FileExists("/exported-skills/.agents/skills/sharpsense-refactoring/SKILL.md").Should().BeTrue();
        fileSystem.FileExists("/exported-skills/.agents/skills/sharpsense-exploring/SKILL.md").Should().BeTrue();
        fileSystem.FileExists("/exported-skills/.agents/skills/sharpsense-impact-analysis/SKILL.md").Should().BeTrue();
        fileSystem.GetFile("/exported-skills/.agents/skills/sharpsense-refactoring/SKILL.md").TextContents.Should().Contain("name: sharpsense-refactoring");
        fileSystem.GetFile("/exported-skills/.agents/skills/sharpsense-exploring/SKILL.md").TextContents.Should().Contain("name: sharpsense-exploring");
        fileSystem.GetFile("/exported-skills/.agents/skills/sharpsense-impact-analysis/SKILL.md").TextContents.Should().Contain("name: sharpsense-impact-analysis");
        console.Output.Should().Contain("/exported-skills/.agents/skills");
        console.Output.Should().NotContain("sharpsense/SKILL.md");
        console.Output.Should().Contain("sharpsense-refactoring/SKILL.md");
        console.Output.Should().Contain("sharpsense-exploring/SKILL.md");
        console.Output.Should().Contain("sharpsense-impact-analysis/SKILL.md");
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
    public async Task WhenRefactorRuns_ThenItUsesTheNewNameOptionAndFormatsToonOutput()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        const string newName = "Updated";
        var ct = TestContext.Current.CancellationToken;
        var refactorSymbolService = new Mock<IRefactorSymbolService>(MockBehavior.Strict);
        refactorSymbolService.Setup(candidate => candidate.RenameSymbol(
                42,
                newName,
                null,
                ct))
            .ReturnsAsync(new RefactorResult(
                true,
                ["src/Fixture.App/MessageConsumer.cs"],
                string.Empty));
        var app = CreateCommandApp(
            console,
            database,
            configureServices: services =>
            {
                services.RemoveAll<IRefactorSymbolService>();
                services.AddScoped<IRefactorSymbolService>(_ => refactorSymbolService.Object);
            });

        var exitCode = await app.RunAsync(
            ["refactor", "--node-id", "42", "--new-name", newName, "--repo-root", RepositoryRoot, "--toon"],
            ct);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            "refactor_success: true" + Environment.NewLine +
            "modified_files:" + Environment.NewLine +
            "  - src/Fixture.App/MessageConsumer.cs");

        refactorSymbolService.Verify(candidate => candidate.RenameSymbol(
            42,
            newName,
            null,
            ct), Times.Once);
    }

    [Fact]
    public async Task WhenRefactorRunsWithTargetOverride_ThenItPassesTheOverrideToTheRefactorSymbolService()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        const string newName = "UpdatedName";
        var ct = TestContext.Current.CancellationToken;
        var refactorSymbolService = new Mock<IRefactorSymbolService>(MockBehavior.Strict);
        refactorSymbolService.Setup(candidate => candidate.RenameSymbol(
                42,
                newName,
                "src/Fixture.App/Fixture.App.csproj",
                ct))
            .ReturnsAsync(new RefactorResult(
                true,
                ["src/Fixture.App/MessageProvider.cs"],
                string.Empty));
        var app = CreateCommandApp(
            console,
            database,
            configureServices: services =>
            {
                services.RemoveAll<IRefactorSymbolService>();
                services.AddScoped<IRefactorSymbolService>(_ => refactorSymbolService.Object);
            });

        var exitCode = await app.RunAsync(
            ["refactor", "--node-id", "42", "--new-name", newName, "--target", "src/Fixture.App/Fixture.App.csproj", "--repo-root", RepositoryRoot, "--toon"],
            ct);

        exitCode.Should().Be(0);
        console.Output.Should().Be(
            "refactor_success: true" + Environment.NewLine +
            "modified_files:" + Environment.NewLine +
            "  - src/Fixture.App/MessageProvider.cs");

        refactorSymbolService.Verify(candidate => candidate.RenameSymbol(
            42,
            newName,
            "src/Fixture.App/Fixture.App.csproj",
            ct), Times.Once);
    }

    [Fact]
    public async Task WhenExecuteRunsWithToon_ThenItFormatsCondensedCommandOutput()
    {
        await using var database = await CliCommandTestDatabase.Create();
        using var console = new TestConsole();
        var ct = TestContext.Current.CancellationToken;
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory.Setup(candidate => candidate.Create(ct))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex.Setup(candidate => candidate.AppendLine("Build succeeded in 13.7s", ct))
            .ReturnsAsync(Result.Ok(1));
        executeLogIndex.Setup(candidate => candidate.FindMatches("Build succeeded", ct))
            .ReturnsAsync(Result.Ok<int[]>([1]));
        executeLogIndex.Setup(candidate => candidate.ReadRange(new ExecutionLineRange(1, 1), ct))
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
                ct))
            .Returns(async (
                CommandProcessRequest _,
                Func<string, CancellationToken, Task> onOutput,
                CancellationToken innerCt) =>
            {
                await onOutput("Build succeeded in 13.7s", innerCt);
                return Result.Ok(new CommandProcessResult(0));
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
        var processRunner = new Mock<ICommandProcessRunner>(MockBehavior.Strict);
        var executeLogIndexFactory = new Mock<IExecuteLogIndexFactory>(MockBehavior.Strict);
        var executeLogIndex = new Mock<IExecuteLogIndex>(MockBehavior.Strict);

        executeLogIndexFactory.Setup(candidate => candidate.Create(ct))
            .ReturnsAsync(executeLogIndex.Object);
        executeLogIndex.Setup(candidate => candidate.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        processRunner.Setup(candidate => candidate.Execute(
                It.Is<CommandProcessRequest>(request =>
                    request.Command == "missing-command" &&
                    request.WorkingDirectory == RepositoryRoot),
                It.IsAny<Func<string, CancellationToken, Task>>(),
                ct))
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
        jsonDocument.RootElement.GetProperty("command").GetString().Should().Be("missing-command");
        jsonDocument.RootElement.GetProperty("status").GetString().Should().Be("error");
        jsonDocument.RootElement.GetProperty("errorMessage").GetString().Should().Be("Failed to start command 'missing-command'.");
    }

    private static Spectre.Console.Cli.CommandApp CreateCommandApp(
        TestConsole console,
        CliCommandTestDatabase database,
        MockFileSystem? fileSystem = null,
        Action<IServiceCollection>? configureServices = null)
    {
        fileSystem ??= new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, RepositoryRoot);

        return Cli.Program.CreateCommandApp(
            console,
            services =>
            {
                services.AddSingleton<IFileSystem>(fileSystem);
                database.ConfigureServices(services);
                configureServices?.Invoke(services);
            },
            enableFileLogging: false);
    }

    private sealed class CliCommandTestDatabase(InMemoryContextFactory contextFactory) : IAsyncDisposable
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
            var contextFactory = new InMemoryContextFactory(new InMemoryContextFactoryOptions(
                UseMigrations: true,
                LoadVectorExtension: true));
            var database = new CliCommandTestDatabase(contextFactory);
            await database.Initialize();
            return database;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IHostedService>();
            contextFactory.ConfigureServices<SharpSenseDbContext>(services);
        }

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
