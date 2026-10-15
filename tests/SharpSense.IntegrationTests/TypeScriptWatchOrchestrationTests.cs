using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Testing;
using System.IO.Abstractions;

namespace SharpSense.IntegrationTests;

public sealed class TypeScriptWatchOrchestrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenConfigAliasChanges_ThenCliWatchRefreshesCompleteTypeScriptScope(bool extendedConfig)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpsense-ts-watch-" + Guid.NewGuid()
                .ToString("N"));
        var databasePath = Path.Combine(root, "test-index.db");
        var previousConsole = AnsiConsole.Console;
        using var console = new TestConsole();
        AnsiConsole.Console = console;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            await File.WriteAllTextAsync(Path.Combine(root, ".git", "HEAD"), "ref: refs/heads/main", ct);
            var configPath = extendedConfig ? "client/base.json" : "client/tsconfig.json";
            if (extendedConfig)
            {
                await Write("client/tsconfig.json", """{ "extends": "./base.json" }""");
            }

            await Write(configPath, Config("v1"));
            await Write(
                "client/src/consumer.ts",
                "import { load } from '@shared/api'; export function consumer() { return load(); }");
            await Write("client/src/deleted.ts", "export function removed() { return 1; }");
            await Write("shared/v1/api.ts", "export function load() { return 'old'; }");
            await Write("shared/v2/api.ts", "export function load() { return 'new'; }");
            await Write("Backend/Feature.cs", "public sealed class Feature {}");

            var catalog = new WorkspaceCatalog(new FileSystem(), Path.Combine(root, ".workspace-home"));
            var selection = catalog.Create(
                "typescript-watch",
                root,
                [new(WorkspaceSourceKind.TypeScript, "client/tsconfig.json")]);
            var workspace = new TestWorkspace(selection.Workspace, databasePath);
            string[] initialPaths = [];
            string[] updatedPaths = [];
            string[] initialTargets = [];
            string[] updatedTargets = [];
            var watcher = new BatchWatcher(async (onBatchChanged, token) =>
            {
                initialPaths = await ReadPaths();
                initialTargets = await ReadConsumerTargets();
                await Write(configPath, Config("v2"));
                var changes = new List<WorkspaceFileChange>
                {
                    new(WorkspaceFileChangeAction.Modified, NewPath: Path.Combine(root, configPath))
                };
                // Extended-config case changes only base.json: no source event can mask lost config routing.
                if (!extendedConfig)
                {
                    File.Delete(Path.Combine(root, "client/src/deleted.ts"));
                    changes.Add(new(
                        WorkspaceFileChangeAction.Deleted,
                        OldPath: Path.Combine(root, "client/src/deleted.ts")));
                    // Watching TS targets must not try to load a tsconfig as a Roslyn workspace.
                    changes.Add(new(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: Path.Combine(root, "Backend/Feature.cs")));
                }

                await onBatchChanged(changes, token);
                updatedPaths = await ReadPaths();
                updatedTargets = await ReadConsumerTargets();
            });
            var app = Cli.Program.CreateCommandApp(
                console,
                services =>
                {
                    services.AddSingleton<IWorkspaceCatalog>(catalog);
                    services.RemoveAll<WorkspaceSelection>();
                    services
                        .AddSingleton(selection with
                        {
                            Workspace = workspace
                        });
                    services.RemoveAll<IRepositoryWorkspace>();
                    services.AddSingleton<IRepositoryWorkspace>(workspace);
                    services.RemoveAll<IWorkspaceWatcher>();
                    services.AddSingleton<IWorkspaceWatcher>(watcher);
                },
                enableFileLogging: false);

            var exit = await app.RunAsync(
                ["analyze", "--workspace", "typescript-watch", "--repo-root", root, "--watch", "--no-embeddings"],
                ct);

            exit.Should().Be(0);
            watcher.Calls.Should().Be(1);
            initialPaths.Should().Contain("client/src/consumer.ts");
            initialPaths.Should().Contain("client/src/deleted.ts");
            initialPaths.Should().Contain("shared/v1/api.ts");
            initialPaths.Should().NotContain("shared/v2/api.ts");
            updatedPaths.Should().Contain("client/src/consumer.ts");
            updatedPaths.Should().Contain("shared/v2/api.ts");
            updatedPaths.Should().NotContain("shared/v1/api.ts");
            updatedPaths.Contains("client/src/deleted.ts").Should().Be(extendedConfig);
            initialTargets.Should().Equal(["code:ts:shared/v1/api.ts:load"]);
            updatedTargets.Should().Equal(["code:ts:shared/v2/api.ts:load"]);
            updatedPaths.Should().NotContain("Backend/Feature.cs");
            console.Output.Should().NotContain("Rebuilding the full index");
            File.Exists(databasePath).Should().BeTrue();

            async Task Write(string relativePath, string content)
            {
                var path = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, ct);
            }

            async Task<string[]> ReadConsumerTargets()
            {
                var options = new DbContextOptionsBuilder<SharpSenseDbContext>()
                    .UseSqlite($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared").Options;
                await using var context = new SharpSenseDbContext(options);

                return await (from edge in context.DependencyEdges
                              join caller in context.GraphNodes on edge.CallerNodeId equals caller.Id
                              join callee in context.GraphNodes on edge.CalleeNodeId equals callee.Id
                              where caller.CanonicalId == "code:ts:client/src/consumer.ts:consumer"
                              select callee.CanonicalId)
                    .ToArrayAsync(ct);
            }

            async Task<string[]> ReadPaths()
            {
                var options = new DbContextOptionsBuilder<SharpSenseDbContext>()
                    .UseSqlite($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared").Options;
                await using var context = new SharpSenseDbContext(options);

                return await context.Documents
                    .Select(document => document.RelativePath)
                    .ToArrayAsync(ct);
            }
        }
        finally
        {
            AnsiConsole.Console = previousConsole;
            using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared");
            SqliteConnection.ClearPool(connection);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string Config(string version)
        => $$"""{ "compilerOptions": { "baseUrl": ".", "paths": { "@shared/*": ["../shared/{{version}}/*"] } }, "include": ["src/**/*.ts"] }""";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenParseFailsAndRecovers_ThenPreservesCommittedGraphUntilSuccessfulAnalysis(bool watch)
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "sharpsense-parse-recovery-" + Guid.NewGuid().ToString("N"));
        var previousConsole = AnsiConsole.Console;
        using var console = new TestConsole();
        AnsiConsole.Console = console;
        string? databasePath = null;

        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "tsconfig.json"), """{ "files": ["app.tsx", "model.ts"] }""", ct);
            await File.WriteAllTextAsync(
                Path.Combine(root, "app.tsx"),
                "import type { Model } from './model'; export const View = () => <div>A & B</div>;",
                ct);
            await File.WriteAllTextAsync(Path.Combine(root, "model.ts"), "export interface Model { name: string; }", ct);
            var catalog = new WorkspaceCatalog(new FileSystem(), Path.Combine(root, "home"));
            var selection = catalog.Create("parser", root, [new(WorkspaceSourceKind.TypeScript, "tsconfig.json")]);
            databasePath = selection.Workspace.DatabasePath;
            PersistedSnapshot? before = null;
            var watcher = new BatchWatcher(async (changed, token) =>
            {
                before = await Snapshot();
                await BreakSource();
                await changed([new(WorkspaceFileChangeAction.Modified, NewPath: Path.Combine(root, "model.ts"))], token);
            });
            string[] arguments = watch
                ? ["analyze", "--workspace", "parser", "--repo-root", root, "--no-embeddings", "--watch"]
                : ["analyze", "--workspace", "parser", "--repo-root", root, "--no-embeddings"];

            if (!watch)
            {
                (await CreateApp().RunAsync(arguments, ct)).Should().Be(0);
                before = await Snapshot();
                await BreakSource();
            }

            (await CreateApp().RunAsync(arguments, ct)).Should().Be(1);

            before.Should().NotBeNull();
            before!.Nodes.Should().HaveCount(3);
            before.Edges.Should().NotBeEmpty();
            var failed = await Snapshot();
            failed.Nodes.Should().Equal(before.Nodes);
            failed.Edges.Should().Equal(before.Edges);
            failed.Revision.Should().Be(before.Revision);
            failed.SuccessfulRun.Should().Be(before.SuccessfulRun);
            console.Output.Should().Contain("model.ts").And.Contain("previous index preserved");

            await File.WriteAllTextAsync(Path.Combine(root, "model.ts"), "export interface Model<out T> { value: T; }", ct);
            (await CreateApp().RunAsync(arguments, ct)).Should().Be(0);

            var recovered = await Snapshot();
            recovered.Revision.Should().NotBe(before.Revision);
            recovered.Nodes.Should().NotEqual(before.Nodes);
            recovered.SuccessfulRun.Should().NotBe(before.SuccessfulRun);

            async Task BreakSource()
            {
                await File.WriteAllTextAsync(Path.Combine(root, "model.ts"), "export interface Model {", ct);
                await File.WriteAllTextAsync(
                    Path.Combine(root, "app.tsx"),
                    "import type { Model } from './model'; export const View = () => <div>Updated & complete</div>;",
                    ct);
            }

            Spectre.Console.Cli.CommandApp CreateApp()
                => Cli.Program.CreateCommandApp(
                    console,
                    services =>
                    {
                        services.AddSingleton<IWorkspaceCatalog>(catalog);
                        services.RemoveAll<IWorkspaceWatcher>();
                        services.AddSingleton<IWorkspaceWatcher>(watcher);
                    },
                    enableFileLogging: false);

            async Task<PersistedSnapshot> Snapshot()
            {
                var options = new DbContextOptionsBuilder<SharpSenseDbContext>()
                    .UseSqlite($"Data Source={databasePath};Mode=ReadWrite;Cache=Shared").Options;
                await using var context = new SharpSenseDbContext(options);
                var nodes = await (from graph in context.GraphNodes
                                   join code in context.CodeNodes on graph.Id equals code.Id
                                   orderby graph.CanonicalId
                                   select new { graph.CanonicalId, code.BodyHash }).ToArrayAsync(ct);
                var edges = await context.DependencyEdges
                    .OrderBy(edge => edge.CallerNodeId)
                    .ThenBy(edge => edge.CalleeNodeId)
                    .ThenBy(edge => edge.EdgeType)
                    .Select(edge => new { edge.CallerNodeId, edge.CalleeNodeId, edge.EdgeType })
                    .ToArrayAsync(ct);
                var state = await context.IndexRunState.SingleAsync(ct);

                return new(
                    nodes
                        .Select(node => $"{node.CanonicalId}|{node.BodyHash}")
                        .ToArray(),
                    edges
                        .Select(edge => $"{edge.CallerNodeId}|{edge.CalleeNodeId}|{edge.EdgeType}")
                        .ToArray(),
                    state.GraphRevision,
                    state.LastSuccessfulIndexJson);
            }
        }
        finally
        {
            AnsiConsole.Console = previousConsole;
            if (databasePath is not null)
            {
                using var connection = new SqliteConnection($"Data Source={databasePath}");
                SqliteConnection.ClearPool(connection);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed record PersistedSnapshot(
        string[] Nodes,
        string[] Edges,
        string Revision,
        string? SuccessfulRun);

    private sealed class BatchWatcher(
        Func<Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task>, CancellationToken, Task> action)
        : IWorkspaceWatcher
    {
        public int Calls { get; private set; }

        public async Task Watch(
            string repositoryRoot,
            Func<IReadOnlyList<WorkspaceFileChange>, CancellationToken, Task> onBatchChanged,
            CancellationToken ct,
            Func<CancellationToken, Task>? initialize = null,
            Action? onReady = null)
        {
            Calls++;
            if (initialize is not null)
            {
                await initialize(ct);
            }

            onReady?.Invoke();
            if (Calls == 1)
            {
                await action(onBatchChanged, ct);
            }
        }
    }

    private sealed class TestWorkspace(IRepositoryWorkspace inner, string databasePath) : IRepositoryWorkspace
    {
        public string RootPath => inner.RootPath;

        public string DatabasePath => databasePath;

        public Guid? WorkspaceId => inner.WorkspaceId;

        public string? WorkspaceName => inner.WorkspaceName;

        public WorkspaceDefinition? Definition => inner.Definition;

        public string ToRepositoryRelativePath(string? filePath) => inner.ToRepositoryRelativePath(filePath);

        public string GetRequiredTargetDirectoryPath(string targetPath) => inner.GetRequiredTargetDirectoryPath(targetPath);

        public bool TryToRepositoryRelativePath(string? filePath, out string relativePath)
            => inner.TryToRepositoryRelativePath(filePath, out relativePath);

        public bool IsSameOrSubPath(string? filePath) => inner.IsSameOrSubPath(filePath);

        public string NormalizeDirectorySeparators(string path) => inner.NormalizeDirectorySeparators(path);
    }
}
