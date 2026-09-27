using SharpSense.Application.GraphStats.Models;

namespace SharpSense.Application.GraphStats.Abstractions;

public interface IIndexRunStore
{
    Task Record(IndexRunSummary run, CancellationToken ct);
}
