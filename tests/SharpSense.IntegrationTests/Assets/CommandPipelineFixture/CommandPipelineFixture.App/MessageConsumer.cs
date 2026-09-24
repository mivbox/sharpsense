using CommandPipelineFixture.Contracts;

namespace CommandPipelineFixture.App;

public sealed class MessageConsumer(IMessageProvider provider)
{
    public string Render()
    {
        return provider.GetMessage();
    }

    public MessageEnvelope RenderEnvelope()
    {
        return new MessageEnvelope(provider.GetMessage());
    }

    public Message MapMessage(MessageModel source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return MessageMapper.ToMessage(source);
    }
}
