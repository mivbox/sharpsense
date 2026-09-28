namespace SharpSense.Application.Shared.Abstractions;

/// <summary>Reads an application view without changing domain state.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>Returns the requested view within the caller's cancellation scope.</summary>
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}
