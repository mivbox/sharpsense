namespace SharpSense.Application.Shared.Abstractions;

/// <summary>Runs one application command and returns its operation-specific outcome.</summary>
public interface ICommandHandler<TCommand, TResult>
{
    /// <summary>Executes the command while preserving the caller's cancellation scope.</summary>
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}
