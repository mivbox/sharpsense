using System.Text.Json;
using System.Text.Json.Serialization;
using SharpSense.Application.Shared.Models;

namespace SharpSense.Cli.Shared;

internal sealed class JsonOutputFormatter : IOutputFormatter
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    static JsonOutputFormatter()
    {
        _serializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public string Format(IEnumerable<CodeNodeResult> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        return JsonSerializer.Serialize(nodes.ToArray(), _serializerOptions);
    }
}
