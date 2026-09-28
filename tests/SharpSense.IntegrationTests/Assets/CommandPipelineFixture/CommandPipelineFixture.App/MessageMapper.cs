using CommandPipelineFixture.Contracts;
using Riok.Mapperly.Abstractions;

namespace CommandPipelineFixture.App;

[Mapper]
public static partial class MessageMapper
{
    public static partial Message ToMessage(MessageModel source);
}
