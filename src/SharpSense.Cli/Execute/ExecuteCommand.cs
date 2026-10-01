using FluentResults;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpSense.Application.CommandExecution;
using SharpSense.Application.CommandExecution.ExecuteProcess.Models;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.CommandExecution;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Text.Json;

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
                : base.Validate();
    }

    protected override int CancellationExitCode => 130;

    protected override void Configure(
        Settings settings,
        IServiceCollection services)
    {
        services.AddSelectedWorkspace(settings);
        services.AddCommandExecution()
            .AddCommandExecutionInfrastructure();
    }

    protected override async Task<int> Execute(
        CommandContext context,
        Settings settings,
        IHost host,
        CancellationToken ct)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExecuteProcessCommand, Result<CommandExecutionResult>>>();
        var result = await handler.Handle(new ExecuteProcessCommand(settings.Command, settings.Query), ct);

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
                        CliJsonOptions.Default));

            return 1;
        }

        CommandOutput.Write(
            context,
            settings.UseToonFormat
                ? TokenObjectNotation.SerializeCommandExecutionResult(result.Value)
                : JsonSerializer.Serialize(result.Value, CliJsonOptions.Default));

        return result.Value.ExitCode;
    }

    private static string GetErrorMessage(IEnumerable<IError> errors)
        => string.Join("; ", errors.Select(static error => error.Message));
}
