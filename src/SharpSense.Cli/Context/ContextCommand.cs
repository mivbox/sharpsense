using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Context360;
using SharpSense.Application.Context360.GetNodeContext.Models;
using SharpSense.Application.Context360.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Context360;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;

namespace SharpSense.Cli.Context;

[UsedImplicitly]
internal sealed class ContextCommand : AbstractAsyncCommand<ContextCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--node-id <NODE_ID>")]
        public int NodeId { get; init; }

        [CommandOption("--repo-root <path>")]
        public string? RepositoryRoot { get; init; }

        public override ValidationResult Validate()
            => NodeId <= 0
                ? ValidationResult.Error("A positive node id is required.")
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
        services.AddSharpSenseConfiguration(rawRoot);
        services.AddContext360();
        services.AddContext360Infrastructure();
        services.AddPersistence();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetNodeContextQuery, Context360Result>>();
        var result = await handler.Handle(
            new GetNodeContextQuery(
                settings.NodeId,
                10),
            ct);

        CommandOutput.Write(context, TokenObjectNotation.SerializeContext360(result));
        return 0;
    }
}
