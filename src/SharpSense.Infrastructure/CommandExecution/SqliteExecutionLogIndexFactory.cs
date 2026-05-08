using Microsoft.Data.Sqlite;
using SharpSense.Application.CommandExecution.Abstractions;

namespace SharpSense.Infrastructure.CommandExecution;

internal sealed class SqliteExecutionLogIndexFactory : IExecutionLogIndexFactory
{
    public async Task<IExecutionLogIndex> Create(CancellationToken ct)
    {
        var connection = new SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Private");
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE VIRTUAL TABLE ExecutionLog
            USING fts5(LineText);
            """;
        await command.ExecuteNonQueryAsync(ct);

        return new SqliteExecutionLogIndex(connection);
    }
}
