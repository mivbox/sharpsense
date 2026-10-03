using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Tests.TestData;
using System.Data.Common;

namespace SharpSense.Infrastructure.Tests.ImpactAnalysis;

public sealed class ImpactAnalyzerConcurrencyTests
{
    [Fact]
    public async Task WhenWriterDeletesCallerDuringTraversal_ThenReturnsOneConsistentSnapshot()
    {
        var directory = Directory.CreateTempSubdirectory("sharpsense-impact-");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory.FullName, "index.db"),
            Pooling = false
        }.ToString();
        var writerOptions = new DbContextOptionsBuilder<SharpSenseDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var pause = new PauseAfterEdges();

        try
        {
            await using var writer = new SharpSenseDbContext(writerOptions);
            await writer.Database.EnsureCreatedAsync(ct);
            await writer.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL", ct);
            await KnowledgeGraphFixture.Seed(writer, ct);
            var readerOptions = new DbContextOptionsBuilder<SharpSenseDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(pause)
                .Options;
            var factory = new Mock<IDbContextFactory<SharpSenseDbContext>>();
            factory
                .Setup(candidate => candidate.CreateDbContextAsync(ct))
                .ReturnsAsync(() => new SharpSenseDbContext(readerOptions));
            var analyzer = new ImpactAnalyzer(factory.Object);
            var analysis = analyzer.Analyze(new ImpactAnalysisQuery(KnowledgeGraphFixture.TargetFullyQualifiedName), ct);

            try
            {
                await pause.Reached.Task.WaitAsync(ct);
                await writer.GraphNodes
                    .Where(node => node.Id == KnowledgeGraphFixture.DirectCallerNodeId)
                    .ExecuteDeleteAsync(ct);
            }
            finally
            {
                pause.Continue.TrySetResult();
            }

            var result = await analysis;

            result.ImpactedNodes.Should().Contain(node => node.Id == KnowledgeGraphFixture.DirectCallerNodeId);
            result.Dependencies.Should().Contain(edge => edge.CallerId == KnowledgeGraphFixture.DirectCallerCanonicalId);
            (await writer.GraphNodes.AnyAsync(node => node.Id == KnowledgeGraphFixture.DirectCallerNodeId, ct)).Should().BeFalse();
        }
        finally
        {
            pause.Continue.TrySetResult();
            directory.Delete(recursive: true);
        }
    }

    private sealed class PauseAfterEdges : DbCommandInterceptor
    {
        private bool _readEdges;
        private bool _paused;

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (_readEdges && !_paused)
            {
                _paused = true;
                Reached.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }

            if (command.CommandText.Contains("FROM \"DependencyEdges\"", StringComparison.Ordinal))
            {
                _readEdges = true;
            }

            return result;
        }
    }
}
