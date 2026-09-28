using CommandPipelineFixture.Contracts;

namespace CommandPipelineFixture.App;

public sealed class MessageProvider : IMessageProvider
{
    private readonly MessageFormatter formatter = new();

    public string Name { get; } = "SharpSense";

    public string GetMessage()
    {
        return formatter.Format(Name);
    }
}
