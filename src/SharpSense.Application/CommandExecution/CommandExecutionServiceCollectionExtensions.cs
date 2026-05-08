using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.CommandExecution.Abstractions;

namespace SharpSense.Application.CommandExecution;

public static class CommandExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddCommandExecution(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ICommandExecutor, CommandExecutor>();
        return services;
    }
}
