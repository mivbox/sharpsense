using Microsoft.EntityFrameworkCore;
using SharpSense.Infrastructure.Storage;
using System.Collections.Concurrent;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Persistence;

/// <summary>
/// Initializes one selected workspace on demand; concurrent requests share initialization.
/// </summary>
internal sealed class WorkspaceDatabaseInitializer(
    IRepositoryWorkspace workspace,
    IDbContextFactory<SharpSenseDbContext> factory,
    IFileSystem fileSystem) : IWorkspaceDatabaseInitializer
{
    private static readonly ConcurrentDictionary<string, InitializationState> _states = new(
        FileSystemPaths.Comparer);

    public async Task Initialize(CancellationToken ct)
    {
        var state = _states.GetOrAdd(workspace.DatabasePath, static _ => new InitializationState());
        await state.Gate.WaitAsync(ct);
        try
        {
            if (!state.Completed || !fileSystem.File.Exists(workspace.DatabasePath))
            {
                await WorkspaceDatabaseMigrator.Initialize(
                    workspace,
                    factory,
                    fileSystem,
                    ct);
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
