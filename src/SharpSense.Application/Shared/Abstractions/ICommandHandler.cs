namespace SharpSense.Application.Shared.Abstractions;

public interface ICommandHandler<TCommand>
{
    Task HandleAsync(TCommand command, CancellationToken ct);
}
