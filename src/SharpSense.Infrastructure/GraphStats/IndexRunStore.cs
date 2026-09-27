using Microsoft.EntityFrameworkCore;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.GraphStats.Models;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;
using System.Text.Json;

namespace SharpSense.Infrastructure.GraphStats;

internal sealed class IndexRunStore(IDbContextFactory<SharpSenseDbContext> dbContextFactory) : IIndexRunStore
{
    public async Task Record(IndexRunSummary run, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(run);
        var json = IndexRunSerialization.Serialize(run);
        _ = IndexRunSerialization.Deserialize(json);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        var state = await dbContext.IndexRunState.SingleOrDefaultAsync(ct);
        if (state is null)
        {
            state = new IndexRunStateRecord();
            dbContext.IndexRunState.Add(state);
        }

        if (IsNewer(run, state.LastAttemptJson))
        {
            state.LastAttemptJson = json;
        }

        if (run.Outcome == "succeeded" && IsNewer(run, state.LastSuccessfulIndexJson))
        {
            state.LastSuccessfulIndexJson = json;
        }

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static bool IsNewer(IndexRunSummary run, string? previousJson)
    {
        try
        {
            return IndexRunSerialization.Deserialize(previousJson) is not
            { } previous ||
                run.CompletedAt >= previous.CompletedAt;
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
