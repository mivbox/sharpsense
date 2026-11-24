namespace SharpSense.Application.Context360.Models;

/// <summary>
/// Total distinct related node counts per Context360 bucket before applying the per-bucket result limit.
/// </summary>
public sealed record Context360Totals(
    int Callers,
    int Implementers,
    int Callees,
    int Inherits,
    int Parents,
    int Children);
