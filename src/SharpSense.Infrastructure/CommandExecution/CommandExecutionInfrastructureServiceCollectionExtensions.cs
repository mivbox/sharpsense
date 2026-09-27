using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpSense.Application.CommandExecution.Abstractions;

namespace SharpSense.Infrastructure.CommandExecution;

public static class CommandExecutionInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCommandExecutionInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContextFactory<TransientExecutionLogDbContext>(options =>
            options.UseSqlite("Data Source=:memory:;Mode=Memory;Cache=Private;Pooling=False"));
        services.TryAddSingleton<ICommandProcessRunner, SystemCommandRunner>();
        services.TryAddSingleton<IExecuteLogIndexFactory, TransientExecutionLogIndexFactory>();

        return services;
    }
}
