using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SharpSense.Infrastructure.Persistence;

public sealed class SharpSenseDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SharpSenseDbContext>
{
    public SharpSenseDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SharpSenseDbContext>();
        var designTimeDatabasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".SharpSense",
            "design-time.db");

        Directory.CreateDirectory(Path.GetDirectoryName(designTimeDatabasePath)!);

        optionsBuilder.UseSqlite(
            $"Data Source={designTimeDatabasePath};Mode=ReadWriteCreate;Cache=Shared",
            sqlite => sqlite.MigrationsAssembly(typeof(SharpSenseDbContext).Assembly.FullName));

        return new SharpSenseDbContext(optionsBuilder.Options);
    }
}
