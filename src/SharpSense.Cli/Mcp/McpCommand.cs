using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using Serilog;
using SharpSense.Application.CommandExecution;
using SharpSense.Application.Context360;
using SharpSense.Application.GraphStats;
using SharpSense.Application.HybridSearch;
using SharpSense.Application.ImpactAnalysis;
using SharpSense.Application.Inheritors;
using SharpSense.Application.Memory;
using SharpSense.Application.Trace;
using SharpSense.Application.Trace.Models;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.CommandExecution;
using SharpSense.Infrastructure.Context360;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Inheritors;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Infrastructure.Trace;
using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpSense.Cli.Mcp;

[UsedImplicitly]
internal sealed class McpCommand : AbstractAsyncCommand<McpCommand.Settings>
{
    private static ILogger Logger => Log.ForContext<McpCommand>();
    private static readonly JsonSerializerOptions _toolSerializerOptions = CreateToolSerializerOptions();

    [UsedImplicitly]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public sealed class Settings : GlobalSettings;

    protected override void Configure(Settings settings, IServiceCollection services)
    {
        Logger.Debug(
            "Configuring MCP command with workspace root: {WorkspaceRoot}",
            settings.WorkspaceRoot);

        services.AddSelectedWorkspace(settings, discoverFromDirectory: true);
        services.AddCommandExecution()
            .AddCommandExecutionInfrastructure(new CommandProcessHost("dotnet", [typeof(Program).Assembly.Location]));
        services.AddContext360();
        services.AddContext360Infrastructure();
        services.AddGraphStats()
            .AddGraphStatsInfrastructure();
        services.AddHybridSearch();
        services.AddHybridSearchInfrastructure();
        services.AddMemory();
        services.AddMemoryInfrastructure();
        services.AddEmbeddingsInfrastructure();
        services.AddInheritors();
        services.AddInheritorsInfrastructure();
        services.AddImpactAnalysis();
        services.AddImpactAnalysisInfrastructure();
        services.AddTrace();
        services.AddTraceInfrastructure();
        services.AddPersistence(initializeOnStartup: false);

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, ct) =>
            {
                if (request.Params?.Name is nameof(SharpSenseMcpTools.semantic_search)
                    or nameof(SharpSenseMcpTools.context)
                    or nameof(SharpSenseMcpTools.trace_node)
                    or nameof(SharpSenseMcpTools.get_inheritors)
                    or nameof(SharpSenseMcpTools.attach_memory)
                    or nameof(SharpSenseMcpTools.delete_memory)
                    or nameof(SharpSenseMcpTools.get_memory)
                    or nameof(SharpSenseMcpTools.get_memories))
                {
                    await request.Services!.GetRequiredService<IWorkspaceDatabaseInitializer>()
                        .Initialize(ct);
                }

                return await next(request, ct);
            }))
            .WithTools<SharpSenseMcpTools>(_toolSerializerOptions);
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        // Resolve selection at startup without opening or migrating its database.
        _ = host.Services.GetRequiredService<WorkspaceSelection>();
        await host.WaitForShutdownAsync(ct);

        return 0;
    }

    private static JsonSerializerOptions CreateToolSerializerOptions()
    {
        var serializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        serializerOptions.Converters.Add(new JsonStringEnumConverter<Domain.KnowledgeGraph.Enums.EdgeCategory>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<TraceDirection>());

        return serializerOptions;
    }
}
