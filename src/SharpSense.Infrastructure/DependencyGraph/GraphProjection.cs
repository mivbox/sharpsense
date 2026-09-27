using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Infrastructure.Indexing.TypeScript;
using System.Data.Common;

namespace SharpSense.Infrastructure.DependencyGraph;

internal static class GraphProjection
{
    public static GraphConnectionNode ReadNode(DbDataReader reader)
    {
        var type = reader.GetString(2);
        var label = reader.GetString(1);
        if (type == "http" && HttpNodeIdentity.TryParse(label, out var method, out var url))
        {
            label = $"{method} {url}";
        }
        else if (type == "package" && PackageNodeIdentity.TryParse(label, out var package, out var export))
        {
            label = string.IsNullOrWhiteSpace(export)
                ? package
                : $"{package}/{export}";
        }

        return new GraphConnectionNode(
            reader.GetInt32(0),
            label,
            type,
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5));
    }

    public static string EdgeType(string storedType)
        => storedType == "HttpRequest" ? "http-request" : storedType.ToLowerInvariant();
}
