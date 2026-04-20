using System.Text.Json.Serialization;

namespace SharpSense.Cli.Mcp;

internal enum TraceDirection
{
    [JsonStringEnumMemberName("caller")]
    Caller,

    [JsonStringEnumMemberName("callee")]
    Callee
}
