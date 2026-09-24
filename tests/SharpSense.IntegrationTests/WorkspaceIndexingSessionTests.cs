using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Ui.Api;
using SharpSense.Cli.Ui.Indexing;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.GraphStats;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceIndexingSessionTests
{
    [Fact]
    public async Task ConcurrentJobsUseIndependentWorkspaceScopesAndDatabases()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"sharpsense-workspace-jobs-{Guid.NewGuid():N}");
        var repositoryRoot = Path.Combine(temporaryDirectory, "repo");
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "docs"));
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "docs", "first.md"), "# First workspace", ct);
        await File.WriteAllTextAsync(Path.Combine(repositoryRoot, "docs", "second.md"), "# Second workspace", ct);

        try
        {
            var catalog = new WorkspaceCatalog(new FileSystem(), Path.Combine(temporaryDirectory, "home"));
            var first = catalog.Create("first", repositoryRoot, [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/first.md")]);
            var second = catalog.Create("second", repositoryRoot, [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/second.md")]);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddWorkspaceUiServices(new WorkspaceUiOptions(null, repositoryRoot, new Uri("http://localhost:50069")));
            services.AddSingleton(catalog);
            services.AddSingleton(Mock.Of<IHostApplicationLifetime>());
            services.AddIndexing();
            services.AddIndexingInfrastructure();
            services.AddEmbeddingsInfrastructure();
            services.AddIndexRunRecording();
            services.AddWorkspaceIndexing();
            await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });
            var coordinator = provider.GetRequiredService<WorkspaceIndexingCoordinator>();

            coordinator.Start(first, new StartWorkspaceIndexingRequest(SkipEmbeddings: true));
            coordinator.Start(second, new StartWorkspaceIndexingRequest(SkipEmbeddings: true));
            await Task.WhenAll(WaitForCompletion(coordinator, first.Definition.Id, ct),
                WaitForCompletion(coordinator, second.Definition.Id, ct));

            Assert.Equal(["docs/first.md"], await DocumentPaths(provider, first, ct));
            Assert.Equal(["docs/second.md"], await DocumentPaths(provider, second, ct));
            Assert.NotEqual(first.Workspace.DatabasePath, second.Workspace.DatabasePath);
            Assert.Equal(1, coordinator.GetStatus(first.Definition.Id).Revision);
            Assert.Equal(1, coordinator.GetStatus(second.Definition.Id).Revision);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task<string[]> DocumentPaths(IServiceProvider provider, WorkspaceSelection selection, CancellationToken ct)
    {
        await using var scope = provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<WorkspaceScope>().Bind(selection);
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>();
        await using var context = await factory.CreateDbContextAsync(ct);
        return await context.Documents.Select(static document => document.RelativePath).ToArrayAsync(ct);
    }

    private static async Task WaitForCompletion(WorkspaceIndexingCoordinator coordinator, Guid id, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            var status = coordinator.GetStatus(id);
            if (status.State == "completed")
            {
                return;
            }

            Assert.NotEqual("failed", status.State);
            await Task.Delay(20, timeout.Token);
        }
    }
}
