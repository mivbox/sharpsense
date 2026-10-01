using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class WorkspaceSession : IDisposable
{
    public MSBuildWorkspace Workspace { get; private set; }
    public Solution ActiveSolution { get; private set; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public Dictionary<string, List<DocumentId>> DocumentIndex { get; private set; }

    public WorkspaceSession(MSBuildWorkspace workspace, Solution activeSolution, IFileSystem fileSystem)
    {
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        ActiveSolution = activeSolution ?? throw new ArgumentNullException(nameof(activeSolution));
        DocumentIndex = BuildIndex(activeSolution, fileSystem);
    }

    public void UpdateActiveSolution(Solution activeSolution) =>
        ActiveSolution = activeSolution ?? throw new ArgumentNullException(nameof(activeSolution));

    public void Replace(MSBuildWorkspace workspace, Solution activeSolution, IFileSystem fileSystem)
    {
        var previousWorkspace = Workspace;
        Workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        ActiveSolution = activeSolution ?? throw new ArgumentNullException(nameof(activeSolution));
        DocumentIndex = BuildIndex(activeSolution, fileSystem);
        previousWorkspace.Dispose();
    }

    private static Dictionary<string, List<DocumentId>> BuildIndex(Solution solution, IFileSystem fileSystem)
    {
        var index = new Dictionary<string, List<DocumentId>>(
            FileSystemPaths.Comparer);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (string.IsNullOrWhiteSpace(document.FilePath))
                {
                    continue;
                }

                var path = fileSystem.Path.GetFullPath(document.FilePath);

                if (!index.TryGetValue(path, out var list))
                {
                    list = [];
                    index[path] = list;
                }
                list.Add(document.Id);
            }
        }

        return index;
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Gate.Dispose();
    }
}
