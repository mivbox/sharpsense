using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.HybridSearch;
using SharpSense.Application.HybridSearch.Models;
using SharpSense.Application.HybridSearch.HybridSearch.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.HybridSearch;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Search;

[UsedImplicitly]
internal sealed class SearchCommand : AbstractAsyncCommand<SearchCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<query>")]
        public string Query { get; init; } = string.Empty;

        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }

        [CommandOption("--toon")]
        public bool UseToonFormat { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(Query)
                ? ValidationResult.Error("A search query is required.")
                : ValidationResult.Success();
    }

    protected override void Configure(Settings settings, IServiceCollection services)
    {
        var rawRoot = CommandPathResolver.ResolveRepositoryRoot(settings.RepositoryRoot);

        services.Configure<SharpSenseCliOptions>(options =>
        {
            options.RepositoryRoot = rawRoot;
        });
        services.AddRepositoryWorkspace(rawRoot);
        services.AddSharpSenseConfiguration(rawRoot);
        services.AddHybridSearch();
        services.AddHybridSearchInfrastructure();
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
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<HybridSearchQuery, HybridSearchResult>>();

        var result = await handler.Handle(
            new HybridSearchQuery(settings.Query),
            ct);
        var output = settings.UseToonFormat
            ? TokenObjectNotation.SerializeSemanticSearch(result.Hits)
            : OutputFormatterFactory.Create(false).Format(HybridSearchHitMapper.Map(result.Hits));

        CommandOutput.Write(context, output);

        return 0;
    }
}
