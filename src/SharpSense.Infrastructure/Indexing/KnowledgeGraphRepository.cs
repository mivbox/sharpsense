using Microsoft.EntityFrameworkCore;
using Serilog;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Infrastructure.Persistence;
using static SharpSense.Infrastructure.Indexing.GraphPersistence;
using static SharpSense.Infrastructure.Indexing.GraphSnapshot;
using static SharpSense.Infrastructure.Indexing.PersistedGraphBuilder;

namespace SharpSense.Infrastructure.Indexing;

internal sealed class KnowledgeGraphRepository(
    IDbContextFactory<SharpSenseDbContext> dbContextFactory)
    : IKnowledgeGraphRepository
{
    // Index-sized collections use EF.Parameter so SQLite receives one JSON parameter,
    // not EF Core 10's default scalar parameter per value, which can exceed its limit.
    private static readonly ILogger _logger = Log.ForContext<KnowledgeGraphRepository>();

    public async Task ReplaceWorkspace(
        ExtractedNodes extractedNodes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(extractedNodes);

        using var trace = SharpSenseTraceSpan.Start("index.persist");

        try
        {
            await using var context = await dbContextFactory.CreateDbContextAsync(ct);
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var normalizedExtractedNodes = CanonicalizeSnapshot(extractedNodes);
            var currentSnapshot = await LoadCurrentSnapshot(context, ct);
            if (AreEquivalentSnapshots(currentSnapshot, normalizedExtractedNodes))
            {
                trace.AddTag("index.persist.skipped", true);
                _logger.Information("Skipping full index persistence because no graph changes were detected.");

                return;
            }

            _logger.Debug(
                "Full index persistence required because {SnapshotDifference}",
                DescribeSnapshotDifference(currentSnapshot, normalizedExtractedNodes));

            var identityMaps = await LoadIdentityMaps(context, ct);
            identityMaps = MatchMovedProjectIdentities(currentSnapshot, normalizedExtractedNodes, identityMaps);
            var persistedGraph = BuildPersistedGraph(normalizedExtractedNodes, identityMaps);

            AddTraceCounts(trace, persistedGraph, normalizedExtractedNodes.Diagnostics.Count);
            await ReplacePersistedGraph(context, persistedGraph, identityMaps, ct);
            await AdvanceGraphRevision(context, ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            throw;
        }
    }

    public async Task<IReadOnlyList<IndexedCodeNode>> GetPersistedCodeNodes(CancellationToken ct)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(ct);

        return await LoadPersistedCodeNodes(context, ct);
    }

    private static void AddTraceCounts(
        SharpSenseTraceSpan trace,
        PersistedGraph graph,
        int diagnosticCount)
    {
        trace.AddTag("index.project.count", graph.ProjectNodes.Length);
        trace.AddTag("index.code_node.count", graph.CodeNodes.Length);
        trace.AddTag("index.document.count", graph.Documents.Length);
        trace.AddTag("index.directory.count", graph.Directories.Length);
        trace.AddTag("index.graph_node.count", graph.GraphNodes.Length);
        trace.AddTag("index.dependency.count", graph.DependencyEdges.Length);
        trace.AddTag("index.diagnostic.count", diagnosticCount);
    }
}
