using CommandPipelineFixture.Contracts;

namespace CommandPipelineFixture.App;

public sealed class ConstructorInjectedConsumer
{
    private readonly IMessageProvider provider;

    public ConstructorInjectedConsumer(IMessageProvider provider)
    {
        this.provider = provider;
    }

    public string Render()
    {
        return provider.GetMessage();
    }
}
