namespace SharpSense.Infrastructure.Memory;

internal static class MemoryTags
{
    public static string[] Normalize(string[]? tags)
        => tags is null
            ? []
            :
            [
                .. tags
                    .Select(static tag => tag.Trim()
                        .ToLowerInvariant())
                    .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static tag => tag, StringComparer.Ordinal)
            ];
}
