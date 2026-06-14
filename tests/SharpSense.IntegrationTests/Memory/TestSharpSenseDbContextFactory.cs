using Microsoft.EntityFrameworkCore;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.IntegrationTests.Memory;

/// <summary>
/// Builds a per-test in-memory SQLite <see cref="SharpSenseDbContext"/> so the memory-layer integration tests run
/// against the real EF Core configuration. The connection is kept open for the lifetime of the factory so the
/// in-memory database survives across <see cref="CreateDbContextAsync"/> calls.
/// </summary>
internal sealed class TestSharpSenseDbContextFactory : IDbContextFactory<SharpSenseDbContext>, IAsyncDisposable
{
    private readonly DbContextOptions<SharpSenseDbContext> _options;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    public TestSharpSenseDbContextFactory()
    {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:;Mode=Memory;Cache=Private;Pooling=False");
        _connection.Open();

        var builder = new DbContextOptionsBuilder<SharpSenseDbContext>();
        builder.UseSqlite(_connection, sqlite => sqlite.MigrationsAssembly(typeof(SharpSenseDbContext).Assembly.FullName));
        _options = builder.Options;

        using var ctx = new SharpSenseDbContext(_options);
        ctx.Database.Migrate();
    }

    public SharpSenseDbContext CreateDbContext() => new(_options);

    public Task<SharpSenseDbContext> CreateDbContextAsync(CancellationToken ct = default)
        => Task.FromResult(new SharpSenseDbContext(_options));

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
