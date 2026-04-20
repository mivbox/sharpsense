using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharpSense.Testkit;

public sealed record InMemoryContextFactoryOptions(
    bool UseMigrations = false,
    bool LoadVectorExtension = false);

public sealed class InMemoryContextFactory : IDisposable, IAsyncDisposable
{
    private readonly InMemoryContextFactoryOptions _options;
    private SqliteConnection? _dbConnection;

    public InMemoryContextFactory(InMemoryContextFactoryOptions? options = null)
    {
        _options = options ?? new InMemoryContextFactoryOptions();
    }

    public TContext GetContext<TContext>(
        Action<string>? writeLine = null)
        where TContext : DbContext
    {
        var context = CreateContext<TContext>(writeLine);
        InitializeDatabase(context);
        return context;
    }

    public async Task<TContext> GetContextAsync<TContext>(
        Action<string>? writeLine = null,
        CancellationToken ct = default)
        where TContext : DbContext
    {
        var context = CreateContext<TContext>(writeLine);
        await InitializeDatabaseAsync(context, ct);
        return context;
    }

    public IDbContextFactory<TContext> CreateDbContextFactory<TContext>(
        Action<string>? writeLine = null)
        where TContext : DbContext
        => new SharedInMemoryDbContextFactory<TContext>(this, writeLine);

    public void ConfigureServices<TContext>(
        IServiceCollection services,
        Action<string>? writeLine = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<TContext>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextFactory<TContext>>();

        var dbContextFactory = CreateDbContextFactory<TContext>(writeLine);
        services.AddSingleton(dbContextFactory);
        services.AddScoped(static serviceProvider => serviceProvider
            .GetRequiredService<IDbContextFactory<TContext>>()
            .CreateDbContext());
    }

    public SqliteConnection GetSqliteConnection() => _dbConnection ??= CreateInMemoryDatabase();

    private SqliteConnection CreateInMemoryDatabase()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        connection.CreateFunction("newid", Guid.NewGuid);
        connection.CreateFunction("newsequentialid", Guid.NewGuid);
        connection.CreateFunction("getDate", () => TimeProvider.System.GetUtcNow().DateTime);
        connection.CreateFunction("getUtcDate", () => TimeProvider.System.GetUtcNow().DateTime);
        connection.CreateFunction("sysdatetimeoffset", () => TimeProvider.System.GetUtcNow());
        connection.Open();

        if (_options.LoadVectorExtension)
        {
            connection.EnableExtensions();
            connection.LoadExtension("vec0");
            connection.EnableExtensions(false);
        }

        return connection;
    }

    private TContext CreateContext<TContext>(Action<string>? writeLine)
        where TContext : DbContext
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        ConfigureOptions<TContext>(optionsBuilder, writeLine);

        return (TContext?)Activator.CreateInstance(typeof(TContext), optionsBuilder.Options)
               ?? throw new InvalidOperationException($"Unable to create DbContext of type {typeof(TContext).FullName}.");
    }

    private void ConfigureOptions<TContext>(
        DbContextOptionsBuilder optionsBuilder,
        Action<string>? writeLine)
        where TContext : DbContext
    {
        optionsBuilder
            .UseSqlite(
                GetSqliteConnection(),
                sqlite => sqlite.MigrationsAssembly(typeof(TContext).Assembly.FullName))
            .EnableDetailedErrors()
            .EnableSensitiveDataLogging();

        if (writeLine is not null)
        {
            optionsBuilder.LogTo(writeLine);
        }
    }

    private void InitializeDatabase(DbContext context)
    {
        if (_options.UseMigrations)
        {
            context.Database.Migrate();
            return;
        }

        context.Database.EnsureCreated();
    }

    private async Task InitializeDatabaseAsync(
        DbContext context,
        CancellationToken ct)
    {
        if (_options.UseMigrations)
        {
            await context.Database.MigrateAsync(ct);
            return;
        }

        await context.Database.EnsureCreatedAsync(ct);
    }

    public void Dispose()
    {
        _dbConnection?.Dispose();
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_dbConnection is not null)
        {
            await _dbConnection.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    private sealed class SharedInMemoryDbContextFactory<TContext>(
        InMemoryContextFactory inMemoryContextFactory,
        Action<string>? writeLine)
        : IDbContextFactory<TContext>
        where TContext : DbContext
    {
        public TContext CreateDbContext()
            => inMemoryContextFactory.GetContext<TContext>(writeLine);

        public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => inMemoryContextFactory.GetContextAsync<TContext>(writeLine, cancellationToken);
    }
}
