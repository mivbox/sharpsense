using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.IntegrationTests;

public sealed class SharpSenseConfigurationExtensionsTests
{
    [Fact]
    public void ConfigurationUsesSelectedHomeWorkspaceAndIgnoresRepositoryYaml()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
            ["/repo/sharpsense.yaml"] = new("invalid: [ yaml")
        }, "/repo");
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("product", "/repo",
            [new(WorkspaceSourceKind.Markdown, "README.md"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSharpSenseConfiguration(selection);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(["README.md", "docs/**/*.md"], provider.GetRequiredService<IOptions<SharpSenseConfig>>().Value.IncludePaths);
        Assert.Same(selection.Workspace, provider.GetRequiredService<IRepositoryWorkspace>());
    }

    [Fact]
    public void WorkspaceResolutionIsDeferredAndAllowsInjectedCatalog()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, "/repo");
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var expected = catalog.Create("product", "/repo", []);
        var services = new ServiceCollection();
        services.AddRepositoryWorkspace("/repo", "product");
        services.AddSharpSenseConfiguration();
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton(catalog);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(expected.Definition.Id, provider.GetRequiredService<IRepositoryWorkspace>().WorkspaceId);
        Assert.Empty(provider.GetRequiredService<IOptions<SharpSenseConfig>>().Value.IncludePaths);
    }

    [Fact]
    public void RunningHostKeepsConfigurationSnapshotUntilWorkspaceIsResolvedAgain()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        }, "/repo");
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("product", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var services = new ServiceCollection();
        services.AddSharpSenseConfiguration(selection);
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<SharpSenseConfig>>();

        catalog.AddSources("product", [new(WorkspaceSourceKind.Markdown, "notes/**/*.md")]);

        Assert.Equal(["docs/**/*.md"], monitor.CurrentValue.IncludePaths);
        Assert.Equal(2, catalog.Resolve("product", "/repo").Definition.Sources.Length);
    }
}
