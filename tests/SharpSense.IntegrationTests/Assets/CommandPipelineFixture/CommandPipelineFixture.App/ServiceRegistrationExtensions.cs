using CommandPipelineFixture.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace CommandPipelineFixture.App;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddMessagePipeline(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IMessageProvider, MessageProvider>();
        services.AddSingleton<MessageConsumer>();
        services.AddSingleton(typeof(MessageFormatter));
        services.AddSingleton(new InferredRegistrationMarker());

        return services;
    }
}
