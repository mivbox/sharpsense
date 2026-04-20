namespace CommandPipelineFixture.Contracts;

public interface IMessageProvider
{
    string Name { get; }

    string GetMessage();
}
