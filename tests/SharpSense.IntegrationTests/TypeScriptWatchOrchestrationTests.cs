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
