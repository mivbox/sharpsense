using Microsoft.Extensions.DependencyInjection;
using SharpSense.Cli.Analyze;
using SharpSense.Cli.Context;
using SharpSense.Cli.Doctor;
using SharpSense.Cli.Execute;
using SharpSense.Cli.Inheritors;
using SharpSense.Cli.Mcp;
using SharpSense.Cli.Memory;
using SharpSense.Cli.Search;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Trace;
using SharpSense.Cli.Ui;
using SharpSense.Cli.Workspaces;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;

namespace SharpSense.Cli;

internal partial class Program
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

            config.UseStrictParsing();
            config.SetApplicationName("sharpsense");
            config.SetApplicationVersion(
                FileVersionInfo.GetVersionInfo(typeof(Program).Assembly.Location).ProductVersion
                ?? typeof(Program).Assembly.GetName().Version?.ToString()
                ?? "unknown");

            AttachData(
                config.AddCommand<WorkspaceCreateCommand>("configure")
                    .WithDescription("Alias for workspace create; prompts for missing inputs in an interactive terminal."),
                executionContext);
            config.AddBranch(
                "workspace",
                workspace =>
            {
                workspace.SetDescription("Create and manage named workspaces stored under ~/.sharpsense.");
                AttachData(
                    workspace.AddCommand<WorkspaceCreateCommand>("create")
                        .WithDescription("Create a workspace, with guided setup for missing inputs in an interactive terminal."),
                    executionContext);
                AttachData(
                    workspace.AddCommand<WorkspaceListCommand>("list")
                        .WithDescription("List registered workspaces without opening their databases."),
                    executionContext);
                AttachData(
                    workspace.AddCommand<WorkspaceListCommand>("ls")
                        .WithDescription("Alias for workspace list."),
                    executionContext);
                AttachData(
                    workspace.AddCommand<WorkspaceUseCommand>("use")
                        .WithDescription("Save the default workspace for CLI commands; --workspace overrides it."),
                    executionContext);
                AttachData(
                    workspace.AddCommand<WorkspaceShowCommand>("show")
                        .WithDescription("Show workspace sources and storage locations."),
                    executionContext);
                AttachData(
                    workspace.AddCommand<WorkspaceRenameCommand>("rename")
                        .WithDescription("Rename a workspace while preserving its identity and index."),
                    executionContext);
                AttachData(
                    workspace.AddCommand<WorkspaceAddCommand>("add")
                        .WithDescription("Add sources to an existing workspace."),
                    executionContext);
                AttachData(
                    workspace.AddCommand<WorkspaceRemoveCommand>("remove")
                        .WithDescription("Remove selected sources; preserve the workspace and its database."),
                    executionContext);
                AttachData(
                    workspace.AddCommand<WorkspaceMergeCommand>("merge")
                        .WithDescription("Create a workspace combining sources from existing workspaces in the same repository."),
                    executionContext);
            });

            var analyze = config
                .AddCommand<AnalyzeCommand>("analyze")
                .WithDescription("Analyze every source in the selected registered workspace.");
            AttachData(analyze, executionContext);

            var doctor = config
                .AddCommand<DoctorCommand>("doctor")
                .WithDescription("Check local indexing prerequisites and inspect the repository index without changing its database.");
            AttachData(doctor, executionContext);

            var contextCommand = config
                .AddCommand<ContextCommand>("context")
                .WithDescription("Show immediate callers, callees, and hierarchy breadth for a node ID.");
            AttachData(contextCommand, executionContext);

            var execute = config
                .AddCommand<ExecuteCommand>("execute")
                .WithDescription("Run a local command and reduce its output into compact excerpts.");
            AttachData(execute, executionContext);

            var inheritors = config
                .AddCommand<InheritorsCommand>("inheritors")
                .WithDescription("List direct inheritors or interface implementers for a node ID.");
            AttachData(inheritors, executionContext);

            var mcp = config
                .AddCommand<McpCommand>("mcp")
                .WithDescription("Start an MCP server over stdio; requires --workspace <name-or-id>.");
            AttachData(mcp, executionContext);

            var memory = config
                .AddCommand<MemoryCommand>("memory")
                .WithDescription("Manage semantic memories: add <node-id>, remove <memory-id>, or list <node-id>.");
            AttachData(memory, executionContext);

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
