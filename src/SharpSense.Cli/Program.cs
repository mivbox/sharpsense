using JetBrains.Annotations;
using Serilog;
using SharpSense.Cli.Shared;

SharpSenseLogging.UseGlobalLogger(isVerbose: false, commandName: null, enableConsoleLogging: false);
var logger = Log.ForContext<Program>();
logger.Information("Configuring SharpSense CLI command application.");

var app = SharpSense.Cli.Program.CreateCommandApp();

logger.Information("SharpSense CLI command application configured successfully.");

try
{
    logger.Information("Running SharpSense CLI.");
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    logger.Error(ex, "SharpSense CLI terminated unexpectedly.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

namespace SharpSense.Cli
{
    [UsedImplicitly]
    public partial class Program;
}
