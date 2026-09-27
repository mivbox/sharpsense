using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.IntegrationTests;

internal static class WorkspaceTestServices
{
    public static WorkspaceSelection AddWorkspaceFixture(
        this IServiceCollection services,
        string repositoryRoot,
        MockFileSystem? fileSystem = null)
    {
        fileSystem ??= new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                [Path.Combine(repositoryRoot, ".git", "HEAD")] = new("ref: refs/heads/main")
            },
            repositoryRoot);
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "sharpsense-test-home"));
        var catalog = new WorkspaceCatalog(fileSystem, home);
        var selection = catalog.Create(
            "fixture",
            repositoryRoot,
            [new WorkspaceSource(WorkspaceSourceKind.Markdown, "**/*.md")]);
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton<IWorkspaceCatalog>(catalog);
        services.AddSingleton(selection);
        services.AddSingleton(selection.Workspace);

        return selection;
    }
}
