using System.Text.Json;
using SharpSense.Application.GraphStats.Models;

namespace SharpSense.Infrastructure.GraphStats;

internal static class IndexRunSerialization
{
    internal const int MaximumJsonLength = 524_288;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(IndexRunSummary run)
    {
        var bounded = run with
        {
            Scope = Limit(run.Scope, 512),
            Phases = (run.Phases ?? [])
                .Take(16)
                .Select(phase => phase with { Name = Limit(phase.Name, 64) })
                .ToArray(),
            Diagnostics = BoundDiagnostics(run.Diagnostics ?? [])
        };

        return JsonSerializer.Serialize(bounded, JsonOptions);
    }

    public static IndexRunSummary? Deserialize(string? json)
    {
        if (json is null)
        {
            return null;
        }

        if (json.Length > MaximumJsonLength)
        {
            throw new JsonException("Stored index history exceeds its size limit.");
        }

        var run = JsonSerializer.Deserialize<IndexRunSummary>(json, JsonOptions)
                  ?? throw new JsonException("Stored index history is empty.");

        if (run.Outcome is not ("succeeded" or "failed" or "cancelled") ||
            run.Kind is not ("full" or "incremental") ||
            string.IsNullOrWhiteSpace(run.Scope) ||
            run.CompletedAt < run.StartedAt ||
            !double.IsFinite(run.DurationMs) || run.DurationMs < 0 ||
            (run.Phases?.Any(phase => phase is null || phase.Name is null ||
                !double.IsFinite(phase.DurationMs) || phase.DurationMs < 0) ?? false) ||
            (run.Diagnostics?.Any(diagnostic => diagnostic is null || diagnostic.Code is null ||
                diagnostic.Severity is null || diagnostic.Message is null) ?? false))
        {
            throw new JsonException("Stored index history has invalid fields.");
        }

        return JsonSerializer.Deserialize<IndexRunSummary>(Serialize(run), JsonOptions);
    }

    private static string Limit(string value, int length)
        => value.Length <= length ? value : value[..(length - 1)] + "…";

    private static IReadOnlyList<IndexDiagnostic> BoundDiagnostics(IReadOnlyList<IndexDiagnostic> diagnostics)
    {
        var retainedCount = diagnostics.Count > 20 ? 19 : 20;
        var bounded = diagnostics
            .OrderBy(diagnostic => diagnostic.Severity == "error" ? 0 : diagnostic.Code == "index-cancelled" ? 1 : 2)
            .Take(retainedCount)
            .Select(diagnostic => diagnostic with
            {
                Code = Limit(diagnostic.Code, 64),
                Severity = Limit(diagnostic.Severity, 16),
                Message = Limit(diagnostic.Message, 1024),
                FilePath = diagnostic.FilePath is null ? null : Limit(diagnostic.FilePath, 512),
                Suggestion = diagnostic.Suggestion is null ? null : Limit(diagnostic.Suggestion, 512)
            })
            .ToList();

        if (diagnostics.Count > 20)
        {
            bounded.Add(new IndexDiagnostic("diagnostics-truncated", "info",
                $"{diagnostics.Count - retainedCount} additional diagnostics were omitted from this bounded report."));
        }

        return bounded;
    }
}
