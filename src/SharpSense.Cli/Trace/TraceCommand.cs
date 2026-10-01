using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.ImpactAnalysis;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.Memory;
using SharpSense.Application.Memory.Abstractions;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Trace;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Trace;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;

namespace SharpSense.Cli.Trace;

[UsedImplicitly]
internal sealed class TraceCommand : AbstractAsyncCommand<TraceCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<identifier>")]
        public string Identifier { get; init; } = string.Empty;

        [CommandOption("-d|--direction <DIRECTION>")]
        public string Direction { get; init; } = "callee";

        [CommandOption("--toon")]
        public bool UseToonFormat { get; init; }

        [CommandOption("--include-structural")]
        public bool IncludeStructural { get; init; }

        [CommandOption("--include-memories")]
        public bool IncludeMemories { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Identifier))
            {
                return ValidationResult.Error("A symbol identifier is required.");
            }

            if (NormalizeDirection(Direction) is null)
            {
                return ValidationResult.Error("Direction must be either 'caller' or 'callee'.");
            }

            return base.Validate();
        }
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.AddImpactAnalysis();
        services.AddImpactAnalysisInfrastructure();
        services.AddTrace();
        services.AddTraceInfrastructure();
        services.AddMemory();
        services.AddMemoryInfrastructure();
        services.AddEmbeddingsInfrastructure();
        services.AddPersistence();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var traceNavigator = services.GetRequiredService<ITraceNavigator>();
        var direction = NormalizeDirection(settings.Direction)
                        ?? throw new InvalidOperationException("Direction validation should have prevented invalid values.");
        var includedEdgeTypes = settings.IncludeStructural
            ? KnowledgeGraphEdgeTypes.All
            : null;
        var rootNode = settings.UseToonFormat || settings.IncludeMemories
            ? await traceNavigator.GetRootNode(settings.Identifier, ct)
            : null;
        CodeNodeResult[] nodes;
        ImpactAnalysisResult? impactResult = null;
        switch (direction)
        {
            case "caller":
                {
                    impactResult = await services
                        .GetRequiredService<IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>>()
                        .Handle(
                            new ImpactAnalysisQuery(
                                settings.Identifier,
                                MaxDepth: 1,
                                IncludeTransitive: false,
                                IncludedEdgeTypes: includedEdgeTypes),
                            ct);

                    nodes = ImpactedNodeMapper.Map(impactResult.ImpactedNodes);
                    break;
                }
            case "callee":
                nodes = await services
                    .GetRequiredService<IQueryHandler<TraceQuery, CodeNodeResult[]>>()
                    .Handle(new TraceQuery(settings.Identifier, includedEdgeTypes), ct);
                break;
            default:
                throw new InvalidOperationException($"Unsupported direction '{direction}'.");
        }

        IReadOnlyDictionary<int, Domain.KnowledgeGraph.Nodes.MemoryNode[]>? memoriesByNodeId = null;
        if (settings.IncludeMemories && rootNode is not null)
        {
            var memoryRepository = services.GetRequiredService<IMemoryRepository>();
            memoriesByNodeId = await memoryRepository.GetNodeMemories(
                [rootNode.Id, .. nodes.Select(static node => node.Id)],
                intents: null,
                ct);
        }

        string output;
        if (settings.UseToonFormat)
        {
            output = rootNode is null
                ? string.Empty
                : direction == "caller"
                    ? TokenObjectNotation.SerializeCallerTrace(
                        rootNode,
                        nodes,
                        impactResult?.Dependencies ?? [],
                        memoriesByNodeId)
                    : TokenObjectNotation.SerializeCalleeTrace(rootNode, nodes, memoriesByNodeId);
        }
        else if (settings.IncludeMemories)
        {
            output = JsonSerializer.Serialize(
                new
                {
                    RootNode = rootNode,
                    Nodes = nodes,
                    MemoriesByNodeId = memoriesByNodeId?.ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value
                            .Select(static memory => new
                            {
                                memory.Id,
                                memory.Intent,
                                memory.IsStale,
                                memory.Tags
                            }))
                },
                CliJsonOptions.Default);
        }
        else
        {
            output = JsonSerializer.Serialize(nodes, CliJsonOptions.Default);
        }

        CommandOutput.Write(context, output);

        return 0;
    }

    private static string? NormalizeDirection(string direction)
        => direction.Trim()
            .ToLowerInvariant() switch
        {
            "caller" => "caller",
            "callee" => "callee",
            _ => null
        };
}
