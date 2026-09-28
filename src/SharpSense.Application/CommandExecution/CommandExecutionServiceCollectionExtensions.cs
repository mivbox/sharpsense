using FluentResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.CommandExecution.ExecuteProcess;
using SharpSense.Application.CommandExecution.ExecuteProcess.Models;
using SharpSense.Application.CommandExecution.Models;
using SharpSense.Application.Shared.Abstractions;

namespace SharpSense.Application.CommandExecution;

public static class CommandExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddCommandExecution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddTransient<ICommandHandler<ExecuteProcessCommand, Result<CommandExecutionResult>>, ExecuteProcessCommandHandler>();

        return services;
    }
}
