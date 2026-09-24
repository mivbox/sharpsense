using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using System.IO.Abstractions;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Persistence;

public sealed class SharpSenseDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SharpSenseDbContext>
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

        optionsBuilder.UseSqlite(
            $"Data Source={designTimeDatabasePath};Mode=ReadWriteCreate;Cache=Shared",
            sqlite => sqlite.MigrationsAssembly(typeof(SharpSenseDbContext).Assembly.FullName));

        return new SharpSenseDbContext(optionsBuilder.Options);
    }
}
