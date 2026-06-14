using System.Data.Common;

namespace SharpSense.Infrastructure.HybridSearch;

internal static class MemorySearchSql
{
    internal static string BuildTagFilterClause(
        DbCommand command,
        string[]? tagFilters,
        string tagsJsonColumn)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagsJsonColumn);

        var normalizedTagFilters = NormalizeTagFilters(tagFilters);
        if (normalizedTagFilters.Length == 0)
        {
            return string.Empty;
        }

        var parameterNames = new string[normalizedTagFilters.Length];
        for (var index = 0; index < normalizedTagFilters.Length; index++)
        {
            var parameterName = $"$tag{index}";
            parameterNames[index] = parameterName;

            var tagParameter = command.CreateParameter();
            tagParameter.ParameterName = parameterName;
            tagParameter.Value = normalizedTagFilters[index];
            command.Parameters.Add(tagParameter);
        }

        return $$"""
            
              AND EXISTS (
                  SELECT 1
                  FROM json_each({{tagsJsonColumn}}) AS tag
                  WHERE lower(CAST(tag.value AS TEXT)) IN ({{string.Join(", ", parameterNames)}})
              )
            """;
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
