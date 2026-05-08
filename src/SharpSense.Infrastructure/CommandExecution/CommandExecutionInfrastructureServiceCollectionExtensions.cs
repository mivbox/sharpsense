using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.CommandExecution.Abstractions;

namespace SharpSense.Infrastructure.CommandExecution;

public static class CommandExecutionInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCommandExecutionInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICommandProcessRunner, SystemCommandRunner>();
        services.TryAddSingleton<IExecutionLogIndexFactory, SqliteExecutionLogIndexFactory>();
        return services;
    }
}
