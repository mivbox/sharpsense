namespace SharpSense.Application.Shared.Abstractions;

public interface ICommandHandler<TCommand>
{
    Task Handle(TCommand command, CancellationToken ct);
}

public interface ICommandHandler<TCommand, TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}
