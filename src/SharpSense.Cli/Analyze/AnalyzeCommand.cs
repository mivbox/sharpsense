using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Cli.Shared;
using SharpSense.Cli.Workspaces;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Analyze;

internal sealed class AnalyzeCommand : AbstractAsyncCommand<AnalyzeCommand.Settings>
{
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--watch")]
        public bool Watch
        {
            get; init;
        }

        [CommandOption("--no-embeddings")]
        public bool SkipEmbeddings
        {
            get; init;
        }

        [CommandOption("--no-cache")]
        public bool DisableEmbeddingCache
        {
            get; init;
        }
    }

    protected override void Configure(Settings settings, IServiceCollection services) => services.AddAnalyzeExecution();

    protected override async Task<int> Execute(CommandContext context, Settings settings, IHost host, CancellationToken ct)
    {
        var selection = await host.Services.GetRequiredService<WorkspaceSetup>()
            .SelectForAnalysis(
            settings.Workspace,
            CommandPathResolver.ResolveWorkspaceRoot(settings.WorkspaceRoot),
            ct);

        return selection is null
            ? 0
            : await host.Services.GetRequiredService<WorkspaceAnalysisRunner>()
                .Run(selection, settings, ct);
    }
}
