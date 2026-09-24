using CommandPipelineFixture.Contracts;

namespace CommandPipelineFixture.App;

public sealed class ServiceLocatorConsumer(IServiceProvider serviceProvider)
{
    public object? ResolveProvider()
    {
        return serviceProvider.GetService(typeof(IMessageProvider));
    }

    public object? ResolveConsumer()
    {
        return serviceProvider.GetService(typeof(MessageConsumer));
    }
}
