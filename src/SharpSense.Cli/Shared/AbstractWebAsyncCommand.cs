using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Shared;

/// <summary>
/// Base class for async commands that require a full ASP.NET Core WebApplication host
/// (e.g., serving UI assets or HTTP APIs).
/// </summary>
public abstract class AbstractWebAsyncCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : GlobalSettings
{
    private static readonly ILogger _logger = Log.ForContext<AbstractWebAsyncCommand<TSettings>>();

    protected abstract void ConfigureServices(TSettings settings, IServiceCollection services);

    protected abstract void ConfigureApp(TSettings settings, WebApplication app);

    protected sealed override async Task<int> ExecuteAsync(
        CommandContext context,
        TSettings settings,
        CancellationToken ct)
    {
        var builder = WebApplication.CreateBuilder();
        var logFilePath = SharpSenseLogging.GetLogFilePath(context.Name);

        builder.Host.UseSerilog((_, cfg) =>
            SharpSenseLogging.ConfigureLogger(cfg, settings.IsVerbose, logFilePath, true));

        _logger.Information(
            "Building web host for command {CommandName} and settings type {SettingsType} with log file {LogFilePath}",
            context.Name,
            typeof(TSettings).FullName,
            logFilePath);
        ConfigureServices(settings, builder.Services);
        _logger.Information("Finished configuring web services for {SettingsType}", typeof(TSettings).FullName);

        await using var app = builder.Build();
        _logger.Information("Web service provider built successfully for {SettingsType}", typeof(TSettings).FullName);

        app.UseSerilogRequestLogging();

        ConfigureApp(settings, app);

        try
        {
            await app.StartAsync(ct);
            _logger.Information("Web host started for {SettingsType}", typeof(TSettings).FullName);
            await app.WaitForShutdownAsync(token: ct);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Web command execution failed for {SettingsType}", typeof(TSettings).FullName);
            throw;
        }
        finally
        {
            _logger.Information("Stopping web host for {SettingsType}", typeof(TSettings).Name);
            await app.StopAsync(CancellationToken.None);
            _logger.Information("Web host stopped for {SettingsType}", typeof(TSettings).Name);
        }
    }
}
