using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Persistence;

internal sealed class SharpSenseDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SharpSenseDbContext>
{
    public SharpSenseDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SharpSenseDbContext>();
        var homeOverride = Environment.GetEnvironmentVariable("SHARPSENSE_HOME");
        var designTimeDirectory = string.IsNullOrWhiteSpace(homeOverride)
            ? Path.GetTempPath()
            : SharpSenseHome.Resolve(new FileSystem(), homeOverride);
        var designTimeDatabasePath = Path.Combine(
            designTimeDirectory,
            "sharpsense-design-time.db");

        Directory.CreateDirectory(designTimeDirectory);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = designTimeDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        optionsBuilder.UseSqlite(
            connectionString,
            sqlite => sqlite.MigrationsAssembly(typeof(SharpSenseDbContext).Assembly.FullName));

        return new SharpSenseDbContext(optionsBuilder.Options);
    }
}
