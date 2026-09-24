using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.CommandExecution.Abstractions;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.CommandExecution;
using SharpSense.Infrastructure.Storage;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace SharpSense.Cli.Execute;

[UsedImplicitly]
internal sealed class ExecuteCommand : AbstractAsyncCommand<ExecuteCommand.Settings>
{
    [UsedImplicitly]
    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<command>")]
        public string Command { get; init; } = string.Empty;

        [CommandOption("-q|--query <QUERY>")]
        public string? Query { get; init; }


        [CommandOption("--toon")]
        public bool UseToonFormat { get; init; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(Command)
                ? ValidationResult.Error("A non-empty command is required.")
                : ValidationResult.Success();
    }

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.AddCommandExecutionInfrastructure();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var processRunner = scope.ServiceProvider.GetRequiredService<ICommandProcessRunner>();
        var executeLogIndexFactory = scope.ServiceProvider.GetRequiredService<IExecuteLogIndexFactory>();
        var cliOptions = scope.ServiceProvider.GetRequiredService<IOptions<SharpSenseCliOptions>>();
        var result = await CommandExecutionReducer.Execute(
            new CommandExecutionRequest(settings.Command, settings.Query),
            processRunner,
            executeLogIndexFactory,
            cliOptions.Value.RepositoryRoot,
            ct);

        if (result.IsFailed)
        {
            var errorMessage = GetErrorMessage(result.Errors);
            CommandOutput.Write(
                context,
                settings.UseToonFormat
                    ? TokenObjectNotation.SerializeCommandExecutionFailure(settings.Command, errorMessage)
                    : JsonSerializer.Serialize(
                        new
                        {
                            command = settings.Command,
                            status = "error",
                            errorMessage
                        },
                        TokenObjectNotation.JsonOptions));
            return 1;
        }

        CommandOutput.Write(
            context,
            settings.UseToonFormat
                ? TokenObjectNotation.SerializeCommandExecutionResult(result.Value)
                : JsonSerializer.Serialize(result.Value, TokenObjectNotation.JsonOptions));
        return result.Value.ExitCode;
    }

    private static string GetErrorMessage(IEnumerable<FluentResults.IError> errors)
        => string.Join("; ", errors.Select(static error => error.Message));
}
