namespace CommandPipelineFixture.App;

public sealed class MessageFormatter
{
    private readonly string prefix = "Hello";

    public string Prefix => prefix;

    public string Format(string input)
        => $"{prefix}, {input}";
}
