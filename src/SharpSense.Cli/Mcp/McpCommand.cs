using ModelContextProtocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Context360;
using SharpSense.Application.GraphStats;
using Serilog;
using SharpSense.Application.Inheritors;
using SharpSense.Application.Shared.Options;
using SharpSense.Application.HybridSearch;
using SharpSense.Application.ImpactAnalysis;
using SharpSense.Application.Memory;
using SharpSense.Application.Trace;
using SharpSense.Infrastructure.Context360;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.CommandExecution;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.Inheritors;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Infrastructure.Trace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Mcp;

[UsedImplicitly]
internal sealed class McpCommand : AbstractAsyncCommand<McpCommand.Settings>
{
    private static ILogger Logger => Log.ForContext<McpCommand>();
    private static readonly JsonSerializerOptions _toolSerializerOptions = CreateToolSerializerOptions();

    [UsedImplicitly]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public sealed class Settings : GlobalSettings
    {
        public override ValidationResult Validate() => string.IsNullOrWhiteSpace(Workspace)
            ? ValidationResult.Error("MCP requires --workspace <name-or-id>; it never uses the CLI default workspace.")
            : base.Validate();
    }

    protected override void Configure(Settings settings, IServiceCollection services)
    {
        Logger.Debug("Configuring MCP command with repository root: {RepositoryRoot}",
            settings.RepositoryRoot);

        services.AddSelectedWorkspace(settings);
        services.AddCommandExecutionInfrastructure();
        services.AddContext360();
        services.AddContext360Infrastructure();
        services.AddGraphStats().AddGraphStatsInfrastructure();
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
        services.AddPersistence();

        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<SharpSenseMcpTools>(_toolSerializerOptions);
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await host.WaitForShutdownAsync(ct);
        return 0;
    }

    private static JsonSerializerOptions CreateToolSerializerOptions()
    {
        var serializerOptions = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);
        serializerOptions.Converters.Add(new JsonStringEnumConverter<SharpSense.Domain.KnowledgeGraph.Enums.EdgeCategory>());
        serializerOptions.Converters.Add(new JsonStringEnumConverter<TraceDirection>());
        return serializerOptions;
    }
}
