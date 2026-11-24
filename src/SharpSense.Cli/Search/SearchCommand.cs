using FluentResults;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.HybridSearch;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Cli.Shared;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.Memory;
using SharpSense.Infrastructure.Persistence;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;

namespace SharpSense.Cli.Search;

[UsedImplicitly]
internal sealed class SearchCommand : AbstractAsyncCommand<SearchCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<query>")]
        public string Query { get; init; } = string.Empty;

        [CommandOption("--toon")]
        public bool UseToonFormat { get; init; }

        [CommandOption("--include-memories")]
        public bool IncludeMemories { get; init; }

        [CommandOption("--limit <LIMIT>")]
        public int Limit { get; init; } = 10;

        [CommandOption("--project <PROJECT>")]
        public string? Project { get; init; }

        [CommandOption("--node-type <TYPE>")]
        public string[]? NodeTypesRaw { get; init; }

        [CommandOption("--tag <TAG>")]
        public string[]? Tags { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Query))
            {
                return ValidationResult.Error("A search query is required.");
            }

            if (Limit is < 1 or > 50)
            {
                return ValidationResult.Error("--limit must be between 1 and 50.");
            }

            if (NodeTypesRaw is { Length: > 0 }
                && NodeTypesRaw.Any(static raw => !TryParseNodeType(raw, out _)))
            {
                return ValidationResult.Error("--node-type values must be valid node types (Class, Interface, Method, Property, Field, Document, Component).");
            }

            return base.Validate();
        }

        internal static bool TryParseNodeType(string raw, out NodeType nodeType)
            => Enum.TryParse(raw, ignoreCase: true, out nodeType)
                && Enum.IsDefined(nodeType)
                && nodeType != NodeType.Memory;
    }

    protected override void Configure(Settings settings, IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.AddHybridSearch();
        services.AddHybridSearchInfrastructure();
        services.AddEmbeddingsInfrastructure();
        services.AddMemoryInfrastructure();
        services.AddPersistence();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<HybridSearchQuery, Result<HybridSearchResult>>>();
        var nodeTypes = settings.NodeTypesRaw is { Length: > 0 }
            ? settings.NodeTypesRaw
                .Select(static raw => Enum.Parse<NodeType>(raw, ignoreCase: true))
                .Distinct()
                .ToArray()
            : null;

        var result = await handler.Handle(
            new HybridSearchQuery(
                settings.Query,
                settings.Limit,
                settings.Project,
                nodeTypes,
                settings.IncludeMemories,
                settings.Tags),
            ct);
        if (result.IsFailed)
        {
            CommandOutput.WriteError(context, string.Join("; ", result.Errors.Select(static error => error.Message)));

            return 1;
        }

        var output = settings.UseToonFormat
            ? TokenObjectNotation.SerializeSemanticSearch(result.Value.Hits)
            : JsonSerializer.Serialize(HybridSearchHitMapper.Map(result.Value.Hits), CliJsonOptions.Default);

        CommandOutput.Write(context, output);

        return 0;
    }
}
