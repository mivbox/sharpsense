using System.Data.Common;

namespace SharpSense.Infrastructure.HybridSearch;

/// <summary>
/// Builds the SQL fragments and DbParameters that gate <c>MemoryNodes</c> keyword and vector searches on the
/// optional tag filter. The clause references parameters named <c>$tag0</c>, <c>$tag1</c>, … in declaration
/// order; callers must bind the matching values via <see cref="AddTagFilterParameters"/>.
/// </summary>
internal static class MemorySearchSql
{
    /// <summary>
    /// Returns the SQL clause that restricts rows to <c>MemoryNodes</c> whose <c>TagsJson</c> array contains
    /// every supplied tag. Returns an empty string when no tag filter is supplied.
    /// </summary>
    internal static string BuildTagFilterClause(string[]? tagFilters, string tagsJsonColumn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagsJsonColumn);

        var normalizedTagFilters = NormalizeTagFilters(tagFilters);
        if (normalizedTagFilters.Length == 0)
        {
            return string.Empty;
        }

        var parameterNames = new string[normalizedTagFilters.Length];
        for (var index = 0; index < normalizedTagFilters.Length; index++)
        {
            parameterNames[index] = $"$tag{index}";
        }

        return $$"""

              AND EXISTS (
                  SELECT 1
                  FROM json_each({{tagsJsonColumn}}) AS tag
                  WHERE lower(CAST(tag.value AS TEXT)) IN ({{string.Join(", ", parameterNames)}})
              )
            """;
    }

    /// <summary>
    /// Binds the normalised tag values to the <c>$tag0</c>, <c>$tag1</c>, … parameters referenced by
    /// <see cref="BuildTagFilterClause"/>. Returns the number of parameters bound.
    /// </summary>
    internal static int AddTagFilterParameters(DbCommand command, string[]? tagFilters)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedTagFilters = NormalizeTagFilters(tagFilters);
        for (var index = 0; index < normalizedTagFilters.Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"$tag{index}";
            parameter.Value = normalizedTagFilters[index];
            command.Parameters.Add(parameter);
        }

        return normalizedTagFilters.Length;
    }

    internal static string[] NormalizeTagFilters(string[]? tagFilters)
        => tagFilters is null
            ? []
            : [..
                tagFilters
                    .Select(static tag => tag.Trim().ToLowerInvariant())
                    .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static tag => tag, StringComparer.Ordinal)];
}
