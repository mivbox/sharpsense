using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SharpSense.Testkit;

public sealed record InMemoryContextFactoryOptions(
    bool UseMigrations = false,
    bool LoadVectorExtension = false);

public sealed class InMemoryContextFactory<TContext> : IDisposable, IAsyncDisposable where TContext : DbContext
{
    private readonly InMemoryContextFactoryOptions _options;
    private readonly Func<DbContextOptions<TContext>, TContext> _createContext;
    private SqliteConnection? _dbConnection;

    public InMemoryContextFactory(Func<DbContextOptions<TContext>, TContext> createContext, InMemoryContextFactoryOptions? options = null)
    {
        _createContext = createContext;
        _options = options ?? new InMemoryContextFactoryOptions();
    }

    public TContext GetContext(
        Action<string>? writeLine = null)
    {
        var context = CreateContext(writeLine);
        InitializeDatabase(context);

        return context;
    }

    public async Task<TContext> GetContext(
        CancellationToken ct,
        Action<string>? writeLine = null)
    {
        var context = CreateContext(writeLine);
        await InitializeDatabase(context, ct);

        return context;
    }

    public IDbContextFactory<TContext> CreateDbContextFactory(
        Action<string>? writeLine = null)
        => new SharedInMemoryDbContextFactory(this, writeLine);

    public void ConfigureServices(
        IServiceCollection services,
        Action<string>? writeLine = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<TContext>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextFactory<TContext>>();

        var dbContextFactory = CreateDbContextFactory(writeLine);
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
        connection.CreateFunction(
            "getDate",
            () => TimeProvider.System.GetUtcNow().DateTime);
        connection.CreateFunction(
            "getUtcDate",
            () => TimeProvider.System.GetUtcNow().DateTime);
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

    private TContext CreateContext(Action<string>? writeLine)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TContext>();
        ConfigureOptions(optionsBuilder, writeLine);

        return _createContext(optionsBuilder.Options);
    }

    private void ConfigureOptions(
        DbContextOptionsBuilder optionsBuilder,
        Action<string>? writeLine)
    {
        optionsBuilder
            .UseSqlite(GetSqliteConnection())
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

    private async Task InitializeDatabase(
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

    private sealed class SharedInMemoryDbContextFactory(
        InMemoryContextFactory<TContext> inMemoryContextFactory,
        Action<string>? writeLine)
        : IDbContextFactory<TContext>
    {
        public TContext CreateDbContext()
            => inMemoryContextFactory.GetContext(writeLine);

        public Task<TContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => inMemoryContextFactory.GetContext(cancellationToken, writeLine);
    }
}
