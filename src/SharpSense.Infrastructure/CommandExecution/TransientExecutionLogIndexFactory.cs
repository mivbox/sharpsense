using Microsoft.EntityFrameworkCore;
using SharpSense.Application.CommandExecution.Abstractions;

namespace SharpSense.Infrastructure.CommandExecution;

internal sealed class TransientExecutionLogIndexFactory(IDbContextFactory<TransientExecutionLogDbContext> dbContextFactory)
    : IExecuteLogIndexFactory
{
    public async Task<IExecuteLogIndex> Create(CancellationToken ct)
    {
        var dbContext = await dbContextFactory.CreateDbContextAsync(ct);

        try
        {
            await dbContext.Database.OpenConnectionAsync(ct);
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE VIRTUAL TABLE ExecutionLog
                USING fts5(LineText);
                """,
                ct);

            return new TransientExecutionLogIndex(dbContext);
        }
        catch
        {
            await dbContext.DisposeAsync();
            throw;
        }
    }
}
