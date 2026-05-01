using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.ImpactAnalysis;
using SharpSense.Application.ImpactAnalysis.Models;
using SharpSense.Application.ImpactAnalysis.ImpactAnalysis.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Application.Trace;
using SharpSense.Application.Trace.Abstractions;
using SharpSense.Application.Trace.Trace.Models;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
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

        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(Identifier) ?
                ValidationResult.Error("A symbol identifier is required.") :
                NormalizeDirection(Direction) is null ?
                    ValidationResult.Error("Direction must be either 'caller' or 'callee'.") :
                    ValidationResult.Success();
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        var rawRoot = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);

        services.Configure<SharpSenseCliOptions>(options =>
        {
            options.RepositoryRoot = rawRoot;
        });
        services.AddRepositoryWorkspace(rawRoot);
        services.AddSharpSenseConfiguration(rawRoot);
        services.AddImpactAnalysis();
        services.AddImpactAnalysisInfrastructure();
        services.AddTrace();
        services.AddTraceInfrastructure();
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
        var rootNode = settings.UseToonFormat
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
                    .Handle(new ImpactAnalysisQuery(settings.Identifier, MaxDepth: 1, IncludeTransitive: false), ct);

                nodes = MapImpactedNodes(impactResult.ImpactedNodes);
                break;
            }
            case "callee":
                nodes = await services
                    .GetRequiredService<IQueryHandler<TraceQuery, CodeNodeResult[]>>()
                    .Handle(new TraceQuery(settings.Identifier), ct);
                break;
            default:
                throw new InvalidOperationException($"Unsupported direction '{direction}'.");
        }

        var output = settings.UseToonFormat
            ? rootNode is null
                ? string.Empty
                : direction == "caller"
                    ? TokenObjectNotation.SerializeCallerTrace(rootNode, nodes, impactResult?.Dependencies ?? [])
                    : TokenObjectNotation.SerializeCalleeTrace(rootNode, nodes)
            : JsonSerializer.Serialize(nodes, TokenObjectNotation.JsonOptions);

        CommandOutput.Write(context, output);
        return 0;
    }

    private static string? NormalizeDirection(string direction)
        => direction.Trim().ToLowerInvariant() switch
        {
            "caller" => "caller",
            "callee" => "callee",
            _ => null
        };

    private static CodeNodeResult[] MapImpactedNodes(IEnumerable<ImpactedCodeNode> impactedNodes)
        => [.. impactedNodes.Select(static node => new CodeNodeResult(
            node.Id,
            node.CanonicalId,
            node.ProjectId,
            node.FullyQualifiedName,
            node.DisplayName,
            node.NodeType,
            node.RelativeFilePath,
            node.StartLine,
            node.EndLine,
            node.Summary))];
}
