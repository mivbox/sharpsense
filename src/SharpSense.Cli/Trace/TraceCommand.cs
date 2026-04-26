using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Features.ImpactAnalysis;
using SharpSense.Application.Features.ImpactAnalysis.Contracts;
using SharpSense.Application.Features.ImpactAnalysis.ImpactAnalysis;
using SharpSense.Application.Features.Trace;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.ImpactAnalysis;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Infrastructure.Trace;
using Spectre.Console;
using Spectre.Console.Cli;

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
        var formatter = OutputFormatterFactory.Create(settings.UseToonFormat);
        var direction = NormalizeDirection(settings.Direction)
                        ?? throw new InvalidOperationException("Direction validation should have prevented invalid values.");
        CodeNodeResult[] nodes;
        switch (direction)
        {
            case "caller":
            {
                var result = await services
                    .GetRequiredService<IQueryHandler<ImpactAnalysisQuery, ImpactAnalysisResult>>()
                    .Handle(new ImpactAnalysisQuery(settings.Identifier, MaxDepth: 1, IncludeTransitive: false), ct);

                nodes = MapImpactedNodes(result.ImpactedNodes);
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

        CommandOutput.Write(context, formatter.Format(nodes));
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
