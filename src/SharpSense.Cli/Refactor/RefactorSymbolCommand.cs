using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Refactoring;
using SharpSense.Application.Refactoring.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Refactoring;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Refactor;

[UsedImplicitly]
internal sealed class RefactorSymbolCommand : AbstractAsyncCommand<RefactorSymbolCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--node-id <NODE_ID>")]
        public int NodeId { get; init; }

        [CommandOption("--new-name <NEW_NAME>")]
        public string NewName { get; init; } = string.Empty;

        [CommandOption("--target <path>")]
        public string? TargetPath { get; init; }

        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }

        public override ValidationResult Validate()
            => NodeId <= 0
                ? ValidationResult.Error("A positive node id is required.")
                : string.IsNullOrWhiteSpace(NewName)
                    ? ValidationResult.Error("A non-empty new name is required.")
                : ValidationResult.Success();
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
        services.AddRefactoring();
        services.AddRefactoringInfrastructure();
        services.AddIndexingInfrastructure();
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
        var result = await services
            .GetRequiredService<IRefactorSymbolService>()
            .RenameSymbol(
                settings.NodeId,
                settings.NewName,
                settings.TargetPath,
                ct);

        CommandOutput.Write(context, TokenObjectNotation.SerializeRefactorResult(result));
        return result.Success ? 0 : 1;
    }
}
