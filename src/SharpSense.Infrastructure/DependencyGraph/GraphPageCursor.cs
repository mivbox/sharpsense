using SharpSense.Application.DependencyGraph.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SharpSense.Infrastructure.DependencyGraph;

internal sealed record GraphPageCursor(
    string Workspace,
    string Scope,
    string Revision,
    string Kind,
    int Phase = 0,
    int NodeId = 0,
    int CallerId = 0,
    int CalleeId = 0,
    string EdgeType = "")
{
    public string Encode()
        => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(this))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static string ScopeHash(IEnumerable<int> directoryIds)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', directoryIds))));

    public static GraphPageCursor? Decode(string? value, string? revision, string workspace, string scope, string kind)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            if (value.Length is 0 or > 2_048 ||
                value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            {
                throw new FormatException();
            }

            var encoded = value.Replace('-', '+')
                .Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            var cursor = JsonSerializer.Deserialize<GraphPageCursor>(Convert.FromBase64String(encoded));
            if (cursor is null || cursor.Workspace != workspace || cursor.Scope != scope || cursor.Kind != kind ||
                string.IsNullOrWhiteSpace(cursor.Revision) || cursor.Phase is < 0 or > 1 ||
                cursor.NodeId < 0 || cursor.CallerId < 0 || cursor.CalleeId < 0 ||
                (kind == "edges" && (!Enum.TryParse<EdgeType>(cursor.EdgeType, out var type) ||
                    !Enum.IsDefined(type) || cursor.EdgeType != type.ToString())))
            {
                throw new FormatException();
            }

            if (revision is not null && revision != cursor.Revision)
            {
                throw new GraphRevisionChangedException();
            }

            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException(
                "Graph cursor is invalid or belongs to another workspace, scope, or page type.",
                nameof(value),
                exception);
        }
    }
}
