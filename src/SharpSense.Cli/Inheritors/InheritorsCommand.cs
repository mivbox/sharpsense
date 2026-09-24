using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.Inheritors;
using SharpSense.Application.Inheritors.GetInheritors.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Inheritors;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;

namespace SharpSense.Cli.Inheritors;

[UsedImplicitly]
internal sealed class InheritorsCommand : AbstractAsyncCommand<InheritorsCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<node-id>")]
        public int NodeId { get; init; }


        [CommandOption("--toon")]
        public bool UseToonFormat { get; init; }

        public override ValidationResult Validate()
            => NodeId <= 0
                ? ValidationResult.Error("A positive node id is required.")
                : ValidationResult.Success();
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.AddInheritors();
        services.AddInheritorsInfrastructure();
        services.AddPersistence();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetInheritorsQuery, CodeNodeResult[]>>();
        var result = await handler.Handle(
            new GetInheritorsQuery(settings.NodeId),
            ct);

        var output = settings.UseToonFormat
            ? ToonOutputFormatter.Format(result)
            : JsonSerializer.Serialize(result, TokenObjectNotation.JsonOptions);

        CommandOutput.Write(context, output);
        return 0;
    }
}
