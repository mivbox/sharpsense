using SharpSense.Application.GraphStats.Models;

namespace SharpSense.Application.GraphStats.Abstractions;

public interface IIndexRunStore
{
    Task RecordAsync(IndexRunSummary run, CancellationToken ct);
}
