using Microsoft.Extensions.Hosting;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.MigrationsHost;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddRepositoryWorkspace(Environment.CurrentDirectory);
        builder.Services.AddPersistence();

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }
}
