namespace SharpSense.Application.Context360.Models;

/// <summary>
/// Selects individual relationship buckets returned by a Context360 query.
/// </summary>
public enum ContextSection
{
    Callers,
    Implementers,
    Callees,
    Inherits,
    Parents,
    Children
}
