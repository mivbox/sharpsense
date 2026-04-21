namespace SharpSense.Application.Shared.Abstractions;

public interface ICommandHandler<TCommand>
{
    Task Handle(TCommand command, CancellationToken ct);
}
