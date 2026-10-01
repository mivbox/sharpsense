using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Storage;

public sealed class RepositoryWorkspaceRegistrationTests
{
    [Fact]
    public void WhenConfiguration_ThenUsesSelectedHomeWorkspaceAndIgnoresRepositoryYaml()
    {
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/sharpsense.yaml"] = new("invalid: [ yaml")
            },
            "/repo");
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create(
            "product",
            "/repo",
            [new(WorkspaceSourceKind.Markdown, "README.md"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddRepositoryWorkspace(selection);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<WorkspaceSelection>().Definition.Sources
            .Select(source => source.Path).Should().Equal(["README.md", "docs/**/*.md"]);
        provider.GetRequiredService<IRepositoryWorkspace>().Should().BeSameAs(selection.Workspace);
    }

    [Fact]
    public void WhenWorkspaceResolution_ThenIsDeferredAndAllowsInjectedCatalog()
    {
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            "/repo");
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var expected = catalog.Create("product", "/repo", []);
        var services = new ServiceCollection();
        services.AddRepositoryWorkspace("product");
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton<IWorkspaceCatalog>(catalog);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRepositoryWorkspace>().WorkspaceId.Should().Be(expected.Definition.Id);
        provider.GetRequiredService<WorkspaceSelection>().Definition.Sources
            .Select(source => source.Path).Should().BeEmpty();
    }

    [Fact]
    public void WhenRunningHost_ThenKeepsConfigurationSnapshotUntilWorkspaceIsResolvedAgain()
    {
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
            },
            "/repo");
        var catalog = new WorkspaceCatalog(fileSystem, "/workspace-home");
        var selection = catalog.Create("product", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var services = new ServiceCollection();
        services.AddRepositoryWorkspace(selection);
        using var provider = services.BuildServiceProvider();
        var snapshot = provider.GetRequiredService<WorkspaceSelection>();

        catalog.AddSources("product", [new(WorkspaceSourceKind.Markdown, "notes/**/*.md")]);

        snapshot.Definition.Sources
            .Select(source => source.Path).Should().Equal(["docs/**/*.md"]);
        catalog.Resolve("product").Definition.Sources.Length.Should().Be(2);
    }
}
