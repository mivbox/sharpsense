namespace SharpSense.Infrastructure.Persistence;

/// <summary>Initializes the selected workspace database before commands or queries use it.</summary>
public interface IWorkspaceDatabaseInitializer
{
    Task Initialize(CancellationToken ct);
}
