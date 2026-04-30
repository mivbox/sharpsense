using Microsoft.Extensions.DependencyInjection;
using SharpSense.Cli.Analyze;
using SharpSense.Cli.Inheritors;
using SharpSense.Cli.Mcp;
using SharpSense.Cli.Search;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Skills;
using SharpSense.Cli.Trace;
using SharpSense.Cli.Ui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpSense.Cli;

public partial class Program
{
    public static CommandApp CreateCommandApp(
        IAnsiConsole? console = null,
        Action<IServiceCollection>? configureServices = null,
        bool enableFileLogging = true)
    {
        var app = new CommandApp();
        ConfigureCommandApp(app, console, configureServices, enableFileLogging);
        return app;
    }

    private static void ConfigureCommandApp(
        ICommandApp app,
        IAnsiConsole? console,
        Action<IServiceCollection>? configureServices,
        bool enableFileLogging)
    {
        ArgumentNullException.ThrowIfNull(app);

        var executionContext = CreateExecutionContext(console, configureServices, enableFileLogging);

        app.Configure(config =>
        {
            if (console is not null)
            {
                config.Settings.Console = console;
            }

            config.SetApplicationName("sharp-sense");

            var index = config
                .AddCommand<AnalyzeCommand>("index")
                .WithDescription("Legacy alias for analyze.");
            AttachData(index, executionContext);

            var analyze = config
                .AddCommand<AnalyzeCommand>("analyze")
                .WithDescription("Analyze and index a target.");
            AttachData(analyze, executionContext);

            var inheritors = config
                .AddCommand<InheritorsCommand>("inheritors")
                .WithDescription("List direct inheritors or interface implementers for a node ID.");
            AttachData(inheritors, executionContext);

            var mcp = config
                .AddCommand<McpCommand>("mcp")
                .WithDescription("Start the MCP server over stdio.");
            AttachData(mcp, executionContext);

            var skills = config
                .AddCommand<SkillsCommand>("skills")
                .WithDescription("Write the embedded SharpSense skills into .agents/skills.");
            AttachData(skills, executionContext);

            var search = config
                .AddCommand<SearchCommand>("search")
                .WithDescription("Search the current repository index as JSON or TOON.");
            AttachData(search, executionContext);

            var trace = config
                .AddCommand<TraceCommand>("trace")
                .WithDescription("Trace callers or callees for a node ID.");
            AttachData(trace, executionContext);

            var ui = config
                .AddCommand<UiCommand>("ui")
                .WithDescription("Start the embedded SharpSense UI.");
            AttachData(ui, executionContext);
        });
    }

    internal static CliCommandExecutionContext? CreateExecutionContext(
        IAnsiConsole? console,
        Action<IServiceCollection>? configureServices,
        bool enableFileLogging)
        => console is null && configureServices is null && enableFileLogging
            ? null
            : new CliCommandExecutionContext(console, configureServices, enableFileLogging);

    private static void AttachData(
        ICommandConfigurator commandConfigurator,
        CliCommandExecutionContext? executionContext)
    {
        ArgumentNullException.ThrowIfNull(commandConfigurator);

        if (executionContext is not null)
        {
            commandConfigurator.WithData(executionContext);
        }
    }
}
