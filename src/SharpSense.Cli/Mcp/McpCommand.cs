using ModelContextProtocol;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SharpSense.Application.Features.HybridSearch;
using SharpSense.Application.Features.ImpactAnalysis;
using SharpSense.Application.Features.Trace;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Infrastructure.Trace;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Mcp;

[UsedImplicitly]
internal sealed class McpCommand : AbstractAsyncCommand<McpCommand.Settings>
{
    private static readonly ILogger _logger = Log.ForContext<McpCommand>();
    private static readonly JsonSerializerOptions _toolSerializerOptions = CreateToolSerializerOptions();

    [UsedImplicitly]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }
    }

    protected override void Configure(Settings settings, IServiceCollection services)
    {
        _logger.Debug("Configuring MCP command with repository root: {RepositoryRoot}",
            settings.RepositoryRoot);

        var workspacePath = !string.IsNullOrWhiteSpace(settings.RepositoryRoot)
            ? settings.RepositoryRoot
            : Environment.CurrentDirectory;

        services.AddRepositoryWorkspace(workspacePath);
        services.AddHybridSearch();
        services.AddHybridSearchInfrastructure();
        services.AddEmbeddingsInfrastructure();
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
        serializerOptions.Converters.Add(new JsonStringEnumConverter<TraceDirection>());
        return serializerOptions;
    }
}
