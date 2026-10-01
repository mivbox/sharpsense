using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpSense.Application.GraphStats;
using SharpSense.Application.GraphStats.GetGraphStats.Models;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.GraphStats;

public sealed class GraphStatsTests
{
    [Fact]
    public async Task WhenDatabaseIsMissing_ThenDiagnosesWithoutCreatingDatabaseOrRequiringPersistenceServices()
    {
        using var fixture = new Fixture();
        var services = new ServiceCollection();
        services.AddSingleton(fixture.Workspace);
        services.AddGraphStats();
        services.AddGraphStatsInfrastructure();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        var result = await provider.GetRequiredService<IQueryHandler<GetGraphStatsQuery, GraphStatsSnapshot>>()
            .Handle(new(), TestContext.Current.CancellationToken);

        result.DatabaseState.Should().Be("missing");
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "database_missing");
        Directory.GetFiles(fixture.Directory.FullName).Should().BeEmpty();
    }

    [Theory]
    [InlineData(".tsx", "TSX")]
    [InlineData(".mdown", "Markdown")]
    [InlineData(".mkd", "Markdown")]
    public async Task WhenCurrentDatabase_ThenReturnsCountsLanguageCoverageAndDurableHistory(
        string extension,
        string language)
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.Initialize();
        await fixture.Seed();
        await using (var context = fixture.CreateDbContext())
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE Documents SET Extension = {extension} WHERE Id = 2;",
                ct);
        }
        var success = Run("succeeded", 1);
        await fixture.Store.Record(success, ct);

        var result = await fixture.Read();

        result.DatabaseState.Should().Be("ready");
        result.IsIndexed.Should().BeTrue();
        result.GraphNodeCount.Should().Be(3);
        result.CodeNodeCount.Should().Be(2);
        result.FileCount.Should().Be(3);
        result.ProjectCount.Should().Be(1);
        result.EdgeCount.Should().Be(1);
        result.MemoryCount.Should().Be(1);
        result.EmbeddedNodeCount.Should().Be(1);
        result.Languages.Should().BeEquivalentTo(new[]
        {
            new LanguageStats("C#", 1, 1, 1),
            new LanguageStats(language, 1, 1, 0),
            new LanguageStats("Other", 1, 0, 0)
        });
        result.EdgeTypes.Should().BeEquivalentTo(new[]
        {
            new EdgeTypeStats("Import", 1)
        });
        result.LastSuccessfulIndex!.CompletedAt.Should().Be(success.CompletedAt);
        result.LastAttempt!.Outcome.Should().Be("succeeded");
        result.Diagnostics.Should().BeEmpty();
        await using var db = fixture.CreateDbContext();
        db.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task WhenFailedAndCancelledAttempts_ThenPreserveLastSuccessAndAuthoredMemory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.Initialize();
        await fixture.Seed();
        var success = Run("succeeded", 1);
        await fixture.Store.Record(success, ct);
        await fixture.Store.Record(
            Run("failed", 2) with
            {
                Diagnostics =
                [
                    new(
                        "parse_failed",
                        "error",
                        "Invalid source syntax.",
                        "Widget.tsx",
                        "Fix source syntax and retry.")
                ]
            },
            ct);
        var failed = await fixture.Read();
        failed.LastAttempt!.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.FilePath == "Widget.tsx");
        failed.LastSuccessfulIndex!.CompletedAt.Should().Be(success.CompletedAt);

        await fixture.Store.Record(Run("cancelled", 3), ct);
        await fixture.Store.Record(Run("failed", 2), ct);
        var cancelled = await fixture.Read();

        cancelled.LastAttempt!.Outcome.Should().Be("cancelled");
        cancelled.LastSuccessfulIndex!.CompletedAt.Should().Be(success.CompletedAt);
        await using var db = fixture.CreateDbContext();
        (await db.MemoryNodes.SingleAsync(ct)).Content.Should().Be("Authored invariant");
        (await db.IndexRunState.CountAsync(ct)).Should().Be(1);
    }

    [Fact]
    public async Task WhenIndexHistoryUpdates_ThenPreserveTransactionalGraphRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.Initialize();
        await fixture.Store.Record(Run("succeeded", 1), ct);
        await fixture.Execute("UPDATE IndexRunState SET GraphRevision = 'graph-commit-revision';");

        await fixture.Store.Record(Run("failed", 2), ct);
        await fixture.Store.Record(Run("cancelled", 3), ct);
        await fixture.Store.Record(Run("succeeded", 4), ct);

        await using var context = fixture.CreateDbContext();
        (await context.IndexRunState
            .Select(state => state.GraphRevision)
            .SingleAsync(ct))
            .Should().Be("graph-commit-revision");
    }

    [Fact]
    public async Task WhenHistoryLimits_ThenBoundDiagnosticCountMessagesAndPhaseCount()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.Initialize();
        await fixture.Store.Record(
            Run("failed", 1) with
            {
                Scope = new string('x', 5000),
                Diagnostics = Enumerable.Range(0, 100)
                    .Select(index => new IndexDiagnostic(
                        "parse_failed",
                        "error",
                        new string('語', 5000),
                        new string('p', 2000),
                        new string('s', 2000)))
                    .ToArray(),
                Phases = Enumerable.Range(0, 50)
                    .Select(index => new IndexPhaseTiming(new string('n', 500), index))
                    .ToArray()
            },
            ct);

        var result = await fixture.Read();

        result.LastAttempt!.Diagnostics.Should().HaveCount(20);
        result.LastAttempt.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Message.Length <= 1024 &&
            (diagnostic.FilePath == null || diagnostic.FilePath.Length <= 512));
        result.LastAttempt.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "diagnostics-truncated");
        result.LastAttempt.Phases.Should().HaveCount(16);
        result.LastAttempt.Scope.Should().HaveLength(512);
        await using var db = fixture.CreateDbContext();
        (await db.IndexRunState.SingleAsync(ct)).LastAttemptJson!
            .Length.Should().BeLessThan(IndexRunSerialization.MaximumJsonLength);
    }

    [Fact]
    public async Task WhenHistoryLimits_ThenPreserveFatalErrorAfterManyExtractionWarnings()
    {
        using var fixture = new Fixture();
        await fixture.Initialize();
        var warnings = Enumerable.Range(0, 50)
            .Select(index => new IndexDiagnostic(
                "extraction-diagnostic",
                "warning",
                $"Warning {index}"));
        await fixture.Store.Record(
            Run("failed", 1) with
            {
                Diagnostics = warnings.Append(new IndexDiagnostic(
                    "index-failed",
                    "error",
                    "Embedding generation failed.",
                    Suggestion: "Check the model assets and retry."))
                    .ToArray()
            },
            TestContext.Current.CancellationToken);

        var result = await fixture.Read();

        result.LastAttempt!.Diagnostics.Should().HaveCount(20);
        result.LastAttempt.Diagnostics!.First().Message.Should().Be("Embedding generation failed.");
    }

    [Fact]
    public async Task WhenLegacyEnumIsDiagnosedBeforeMaterialization_ThenPreservesDatabase()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.Initialize();
        await fixture.Seed();
        await fixture.Execute("UPDATE DependencyEdges SET EdgeType = 'DocumentHierarchy';");
        var before = await File.ReadAllBytesAsync(fixture.Workspace.DatabasePath, ct);

        var result = await fixture.Read();

        result.DatabaseState.Should().Be("incompatible");
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Message.Contains("DocumentHierarchy") && diagnostic.Suggestion!.Contains("authored memories"));
        (await File.ReadAllBytesAsync(fixture.Workspace.DatabasePath, ct)).Should().Equal(before);
        await using var db = fixture.CreateDbContext();
        (await db.MemoryNodes.SingleAsync(ct)).Content.Should().Be("Authored invariant");
    }

    [Fact]
    public async Task WhenMigrationIsUnknown_ThenDiagnosesWithoutApplyingOrResettingSchema()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.Initialize();
        await fixture.Seed();
        await fixture.Execute("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('20990101000000_Unknown', '99.0.0');");

        var result = await fixture.Read();

        result.DatabaseState.Should().Be("incompatible");
        await using var db = fixture.CreateDbContext();
        (await db.MemoryNodes.SingleAsync(ct)).Content.Should().Be("Authored invariant");
        (await db.Database.GetAppliedMigrationsAsync(ct)).Should().Contain("20990101000000_Unknown");
    }

    [Fact]
    public async Task WhenPreviousSchema_ThenReturnsCountsAndUpgradeGuidanceWithoutMigrating()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.Initialize("20260923000200_MemoryStableNodeIdentity");
        await fixture.Seed();

        var result = await fixture.Read();

        result.DatabaseState.Should().Be("upgrade_required");
        result.CodeNodeCount.Should().Be(2);
        result.LastSuccessfulIndex.Should().BeNull();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "schema_upgrade_required");
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "index_history_unavailable");
        await using var db = fixture.CreateDbContext();
        (await db.Database.GetAppliedMigrationsAsync(ct)).Should().NotContain("20260924000000_IndexRunState");

        await db.Database.MigrateAsync(ct);
        (await db.MemoryNodes.SingleAsync(ct)).Content.Should().Be("Authored invariant");
        (await fixture.Read()).DatabaseState.Should().Be("ready");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"startedAt\":\"2026-09-24T00:00:00Z\",\"completedAt\":\"2026-09-24T00:00:01Z\",\"durationMs\":1000,\"outcome\":\"failed\",\"kind\":\"full\",\"scope\":\"SharpSense.sln\",\"diagnostics\":[null]}")]
    public async Task WhenInvalidHistory_ThenReturnsGraphCountsAndWarning(string json)
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await fixture.Initialize();
        await fixture.Seed();
        await fixture.Store.Record(Run("succeeded", 1), ct);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE IndexRunState SET LastAttemptJson = {json};",
                ct);
        }

        var result = await fixture.Read();

        result.CodeNodeCount.Should().Be(2);
        result.LastAttempt.Should().BeNull();
        result.LastSuccessfulIndex.Should().NotBeNull();
        result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "index_history_unreadable");
    }

    [Fact]
    public async Task WhenInvalidDatabase_ThenReturnsReadableDiagnosticWithoutReplacingFile()
    {
        var ct = TestContext.Current.CancellationToken;
        using var fixture = new Fixture();
        await File.WriteAllTextAsync(fixture.Workspace.DatabasePath, "Not a SQLite database", ct);

        var result = await fixture.Read();

        result.DatabaseState.Should().Be("unreadable");
        (await File.ReadAllTextAsync(fixture.Workspace.DatabasePath, ct)).Should().Be("Not a SQLite database");
    }

    private static IndexRunSummary Run(string outcome, int minute)
    {
        var start = new DateTimeOffset(2026, 9, 24, 0, minute, 0, TimeSpan.Zero);

        return new(
            start,
            start.AddSeconds(2),
            2000,
            outcome,
            "full",
            "SharpSense.sln",
            2,
            1,
            1,
            [new("extraction", 1000), new("embeddings", 500), new("persistence", 500)],
            []);
    }

    private sealed class Fixture : IDbContextFactory<SharpSenseDbContext>, IDisposable
    {
        public DirectoryInfo Directory { get; } = System.IO.Directory.CreateTempSubdirectory("sharpsense-graph-stats-");

        public IRepositoryWorkspace Workspace { get; }

        public IndexRunStore Store => new(this);

        public Fixture()
        {
            var workspace = new Mock<IRepositoryWorkspace>();
            workspace
                .SetupGet(value => value.RootPath)
                .Returns(Directory.FullName);
            workspace
                .SetupGet(value => value.DatabasePath)
                .Returns(Path.Combine(Directory.FullName, "graph.db"));
            Workspace = workspace.Object;
        }

        public SharpSenseDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<SharpSenseDbContext>()
                .UseSqlite($"Data Source={Workspace.DatabasePath};Pooling=False")
                .AddInterceptors(new SqlitePragmaInterceptor()).Options);

        public async Task Initialize(string? migration = null)
        {
            await using var db = CreateDbContext();
            await db.GetService<IMigrator>()
                .MigrateAsync(migration, TestContext.Current.CancellationToken);
        }

        public async Task Execute(string sql)
        {
            await using var db = CreateDbContext();
            await db.Database.ExecuteSqlRawAsync(sql, TestContext.Current.CancellationToken);
        }

        public Task Seed() => Execute("""
            INSERT INTO Directories (Id, ParentId, Path, Name) VALUES (1, NULL, '', '');
            INSERT INTO Documents (Id, DirectoryId, FileName, Extension, RelativePath, Kind)
                VALUES (1, 1, 'Widget.cs', '.cs', 'Widget.cs', 'Source'),
                       (2, 1, 'Widget.tsx', '.tsx', 'Widget.tsx', 'Source'),
                       (3, 1, 'Project.csproj', '.csproj', 'Project.csproj', 'ProjectFile');
            INSERT INTO GraphNodes (Id, CanonicalId, Kind)
                VALUES (1, 'code:Widget', 'Code'), (2, 'code:WidgetComponent', 'Code'), (3, 'project:Project', 'Project');
            INSERT INTO ProjectNodes (Id, Name, ProjectDocumentId, ContentHash) VALUES (3, 'Project', 3, 'project-hash');
            INSERT INTO CodeNodes (Id, ProjectNodeId, DocumentId, FullyQualifiedName, DisplayName, NodeType,
                StartLine, EndLine, Summary, SearchText, BodyHash, VectorEmbedding)
                VALUES (1, 3, 1, 'Widget', 'Widget', 'Class', 1, 3, 'Widget', 'Widget', 'body', X'0000803F'),
                       (2, NULL, 2, 'WidgetComponent', 'WidgetComponent', 'Component', 1, 3, 'Widget', 'Widget', 'body', NULL);
            INSERT INTO DependencyEdges (CallerNodeId, CalleeNodeId, EdgeType) VALUES (1, 2, 'Import');
            INSERT INTO MemoryNodes (Id, TargetCodeNodeId, TargetCodeHash, Content, ContentHash,
                TagsJson, Intent, VectorEmbedding, CreatedAt)
                VALUES ('F11E5391-64A7-4F5A-B85D-6CC48B9F18C1', 1, 'body', 'Authored invariant',
                'content-hash', '[]', 'Invariant', NULL, '2026-09-24 00:00:00+00:00');
            """);

        public Task<GraphStatsSnapshot> Read() => new GraphStatsReader(Workspace).Read(TestContext.Current.CancellationToken);

        public void Dispose() => Directory.Delete(recursive: true);
    }
}
