using System.Text.Json.Serialization;

namespace SharpSense.Application.Trace.Models;

/// <summary>Identifies upstream callers or downstream callees in a trace.</summary>
public enum TraceDirection
{
    [JsonStringEnumMemberName("caller")]
    Caller,

    [JsonStringEnumMemberName("callee")]
    Callee
}
