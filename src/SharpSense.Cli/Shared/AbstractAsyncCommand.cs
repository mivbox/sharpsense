using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Shared;

public abstract class AbstractAsyncCommand<TSettings> : AsyncCommand<TSettings>
    where TSettings : GlobalSettings
{
    protected abstract void Configure(TSettings settings, IServiceCollection services);

    protected abstract Task<int> Execute(
        CommandContext context,
        TSettings settings,
        IHost host,
        CancellationToken ct);

    protected sealed override async Task<int> ExecuteAsync(
        CommandContext context,
        TSettings settings,
        CancellationToken ct)
    {
        var executionContext = CommandOutput.GetExecutionContext(context);
        var enableFileLogging = executionContext?.EnableFileLogging ?? true;
        var previousLogger = Log.Logger;
        SharpSenseLogging.UseGlobalLogger(settings.IsVerbose, context.Name, enableConsoleLogging: false, enableFileLogging);

        var builder = Host.CreateApplicationBuilder();
        var logFilePath = enableFileLogging ? SharpSenseLogging.GetLogFilePath(context.Name) : null;

        try
        {
            builder.Services.AddSerilog((_, cfg) =>
                SharpSenseLogging.ConfigureLogger(cfg, settings.IsVerbose, logFilePath, false, enableFileLogging));

            Log.Information(
                "Building host for command {CommandName} and settings type {SettingsType} with log file {LogFilePath}",
                context.Name,
                typeof(TSettings).FullName,
                logFilePath ?? "<disabled>");
            Configure(settings, builder.Services);
            executionContext?.ConfigureServices?.Invoke(builder.Services);
            Log.Information("Finished configuring services for {SettingsType}", typeof(TSettings).FullName);

            using var host = builder.Build();
            Log.Information("Service provider built successfully for {SettingsType}", typeof(TSettings).FullName);
            await host.StartAsync(ct);
            Log.Information("Host started for {SettingsType}", typeof(TSettings).FullName);

            try
            {
                return await Execute(
                    context,
                    settings,
                    host,
                    ct);
            }
            finally
            {
                Log.Information("Stopping host for {SettingsType}", typeof(TSettings).Name);
                await host.StopAsync(CancellationToken.None);
                Log.Information("Host stopped for {SettingsType}", typeof(TSettings).Name);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception e)
        {
            Log.Error(
                e,
                "Command execution failed for {SettingsType}",
                typeof(TSettings).FullName);

            CommandOutput.WriteError(context, e.GetBaseException().Message);

            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
            Log.Logger = previousLogger;
        }
    }
}
