using Microsoft.EntityFrameworkCore;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Testkit;

namespace SharpSense.IntegrationTests.Memory;

internal sealed class TestSharpSenseDbContextFactory : IDbContextFactory<SharpSenseDbContext>, IAsyncDisposable
{
    private readonly InMemoryContextFactory<SharpSenseDbContext> _database = new(
        options => new SharpSenseDbContext(options),
        new(UseMigrations: true));

    public SharpSenseDbContext CreateDbContext() => _database.GetContext();

    public Task<SharpSenseDbContext> CreateDbContextAsync(CancellationToken ct = default) => _database.GetContext(ct);

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
