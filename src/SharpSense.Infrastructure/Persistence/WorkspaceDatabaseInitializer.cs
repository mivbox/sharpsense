using System.Collections.Concurrent;
using System.IO.Abstractions;
using Microsoft.EntityFrameworkCore;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Persistence;

/// <summary>
/// Initializes one selected workspace on demand; concurrent requests share initialization.
/// </summary>
public sealed class WorkspaceDatabaseInitializer(
    IRepositoryWorkspace workspace,
    IDbContextFactory<SharpSenseDbContext> factory,
    IFileSystem fileSystem)
{
    private static readonly ConcurrentDictionary<string, InitializationState> States = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public async Task InitializeAsync(CancellationToken ct)
    {
        var state = States.GetOrAdd(workspace.DatabasePath, static _ => new InitializationState());
        await state.Gate.WaitAsync(ct);
        try
        {
            if (!state.Completed || !fileSystem.File.Exists(workspace.DatabasePath))
            {
                await PersistenceServiceCollectionExtensions.EfCoreEnsureDatabase.InitializeAsync(workspace, factory, fileSystem, ct);
                state.Completed = true;
            }
        }
        finally
        {
            state.Gate.Release();
        }
    }

    private sealed class InitializationState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public bool Completed { get; set; }
    }
}
