using System.IO.Abstractions.TestingHelpers;
using System.IO.Abstractions;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Storage;

public sealed class WorkspaceCatalogTests
{
    [Fact]
    public void CreatingWorkspacePersistsHomeDefinitionWithoutCreatingDatabaseOrRepositoryConfig()
    {
        var (fileSystem, catalog) = CreateCatalog();

        var selection = catalog.Create("product", "/repo", [
            new(WorkspaceSourceKind.CSharp, "backend/Orders.csproj"),
            new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json"),
            new(WorkspaceSourceKind.Markdown, "docs/**/*.md")
        ]);
        var loaded = catalog.Resolve("product", "/elsewhere");

        Assert.NotEqual(Guid.Empty, loaded.Definition.Id);
        Assert.Equal(selection.Definition.Id, loaded.Definition.Id);
        Assert.Equal("/repo", loaded.Workspace.RootPath);
        Assert.Equal($"/home/sharpsense/workspaces/{loaded.Definition.Id:D}/index.db", loaded.Workspace.DatabasePath);
        Assert.Equal(3, loaded.Definition.Sources.Length);
        Assert.Equal(loaded.Definition.Id, loaded.Workspace.WorkspaceId);
        Assert.Equal("product", loaded.Workspace.WorkspaceName);
        Assert.True(fileSystem.File.Exists(loaded.ConfigurationPath));
        Assert.False(fileSystem.File.Exists(loaded.Workspace.DatabasePath));
        Assert.False(fileSystem.File.Exists("/repo/sharpsense.yaml"));
        Assert.DoesNotContain(fileSystem.AllFiles, static path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void ResolutionFromSubdirectoryFindsUniqueRegisteredRepositoryWorkspace()
    {
        var (_, catalog) = CreateCatalog();
        var expected = catalog.Create("product", "/repo", []);

        var selected = catalog.Resolve(null, "/repo/frontend");

        Assert.Equal(expected.Definition.Id, selected.Definition.Id);
        Assert.Equal(expected.Definition.Id, catalog.Resolve(expected.Definition.Id.ToString(), "/other").Definition.Id);
    }

    [Fact]
    public void MultipleWorkspaceSelectionRequiresExplicitName()
    {
        var (_, catalog) = CreateCatalog();
        catalog.Create("frontend", "/repo", []);
        catalog.Create("backend", "/repo", []);

        var exception = Assert.Throws<InvalidOperationException>(() => catalog.Resolve(null, "/repo/frontend"));

        Assert.Contains("--workspace", exception.Message);
        Assert.Contains("frontend", exception.Message);
        Assert.Contains("backend", exception.Message);
        Assert.Equal("frontend", catalog.Resolve("FRONTEND", "/repo").Definition.Name);
    }

    [Fact]
    public void MissingWorkspaceResolutionDoesNotCreateStorage()
    {
        var (fileSystem, catalog) = CreateCatalog();

        Assert.Empty(catalog.List());
        var exception = Assert.Throws<InvalidOperationException>(() => catalog.Resolve(null, "/repo"));

        Assert.Contains("sharpsense configure", exception.Message);
        Assert.False(fileSystem.Directory.Exists(catalog.HomeDirectory));
    }

    [Fact]
    public void WorkspacesInSameRepositoryHaveIndependentDatabasePaths()
    {
        var (_, catalog) = CreateCatalog();

        var frontend = catalog.Create("frontend", "/repo", []);
        var backend = catalog.Create("backend", "/repo", []);

        Assert.NotEqual(frontend.Workspace.DatabasePath, backend.Workspace.DatabasePath);
        Assert.Equal(frontend.Workspace.RootPath, backend.Workspace.RootPath);
    }

    [Fact]
    public void MergeCopiesAndDeduplicatesSourcesWithoutSharingMutableDefinitions()
    {
        var (_, catalog) = CreateCatalog();
        WorkspaceSource docs = new(WorkspaceSourceKind.Markdown, "docs/**/*.md");
        WorkspaceSource frontendSource = new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json");
        catalog.Create("frontend", "/repo", [frontendSource, docs]);
        catalog.Create("backend", "/repo", [new(WorkspaceSourceKind.CSharp, "backend/Orders.csproj"), docs]);

        var merged = catalog.Merge("product", ["frontend", "backend"]);
        catalog.RemoveSources("frontend", [frontendSource]);

        Assert.Equal(3, merged.Definition.Sources.Length);
        Assert.Single(merged.Definition.Sources, source => source == docs);
        Assert.Contains(frontendSource, catalog.Resolve("product", "/repo").Definition.Sources);
        Assert.DoesNotContain(frontendSource, catalog.Resolve("frontend", "/repo").Definition.Sources);
    }

    [Fact]
    public void MergeRejectsDifferentRepositoryRoots()
    {
        var (fileSystem, catalog) = CreateCatalog();
        fileSystem.AddDirectory("/other/.git");
        catalog.Create("first", "/repo", []);
        catalog.Create("second", "/other", []);

        var exception = Assert.Throws<InvalidOperationException>(() => catalog.Merge("combined", ["first", "second"]));

        Assert.Contains("same repository", exception.Message);
        Assert.Equal(2, catalog.List().Count);
    }

    [Fact]
    public void AddAndRemoveNormalizePathsAndPreserveWorkspaceIdentity()
    {
        var (_, catalog) = CreateCatalog();
        var created = catalog.Create("product", "/repo", []);

        var updated = catalog.AddSources("product", [
            new(WorkspaceSourceKind.CSharp, "./backend/Orders.csproj"),
            new(WorkspaceSourceKind.CSharp, "/repo/backend/Orders.csproj")
        ]);
        var removed = catalog.RemoveSources("product", [new(WorkspaceSourceKind.CSharp, "backend/Orders.csproj")]);

        Assert.Equal(created.Definition.Id, updated.Definition.Id);
        Assert.Equal(created.Definition.Id, removed.Definition.Id);
        Assert.Equal("backend/Orders.csproj", Assert.Single(updated.Definition.Sources).Path);
        Assert.Empty(removed.Definition.Sources);
    }

    [Theory]
    [InlineData("../outside.csproj")]
    [InlineData("/elsewhere/outside.csproj")]
    [InlineData("backend/../../outside.csproj")]
    public void SourcesCannotEscapeRegisteredRepository(string path)
    {
        var (fileSystem, catalog) = CreateCatalog();

        Assert.Throws<ArgumentException>(() => catalog.Create("product", "/repo", [new(WorkspaceSourceKind.CSharp, path)]));

        Assert.False(fileSystem.Directory.Exists(catalog.HomeDirectory));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("one/two")]
    [InlineData(".")]
    [InlineData("../../")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public void WorkspaceNamesCannotBePathsOrIds(string name)
    {
        var (_, catalog) = CreateCatalog();

        Assert.Throws<ArgumentException>(() => catalog.Create(name, "/repo", []));
    }

    [Fact]
    public void DuplicateNamesAreRejectedCaseInsensitively()
    {
        var (_, catalog) = CreateCatalog();
        catalog.Create("product", "/repo", []);

        Assert.Throws<InvalidOperationException>(() => catalog.Create("PRODUCT", "/repo", []));
        Assert.Single(catalog.List());
    }

    [Fact]
    public void DefinitionCannotRedirectWorkspaceStorageThroughItsId()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var created = catalog.Create("product", "/repo", []);
        fileSystem.File.WriteAllText(created.ConfigurationPath,
            fileSystem.File.ReadAllText(created.ConfigurationPath).Replace(created.Definition.Id.ToString(), Guid.NewGuid().ToString()));

        Assert.Empty(catalog.List());
        var exception = Assert.Throws<InvalidOperationException>(() => catalog.ResolveById(created.Definition.Id));

        Assert.Contains("ID must match", exception.Message);
    }

    [Fact]
    public void UnsupportedConfigurationVersionIsActionable()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var created = catalog.Create("product", "/repo", []);
        fileSystem.File.WriteAllText(created.ConfigurationPath,
            fileSystem.File.ReadAllText(created.ConfigurationPath).Replace("version: 1", "version: 999"));

        Assert.Empty(catalog.List());
        var exception = Assert.Throws<InvalidOperationException>(() => catalog.ResolveById(created.Definition.Id));

        Assert.Contains("unsupported", exception.Message);
        Assert.Contains(created.ConfigurationPath, exception.Message);
    }

    [Fact]
    public void MalformedSiblingDoesNotBlockHealthyResolutionOrCreation()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var healthy = catalog.Create("healthy", "/repo", []);
        var broken = catalog.Create("broken", "/repo", []);
        const string invalid = "private: [ malformed configuration";
        fileSystem.File.WriteAllText(broken.ConfigurationPath, invalid);

        Assert.Equal(healthy.Definition.Id, Assert.Single(catalog.List()).Definition.Id);
        Assert.Equal(healthy.Definition.Id, catalog.Resolve("healthy", "/repo").Definition.Id);
        Assert.Equal(healthy.Definition.Id, catalog.ResolveById(healthy.Definition.Id).Definition.Id);
        Assert.Equal(healthy.Definition.Id, catalog.Resolve(null, "/repo").Definition.Id);
        var created = catalog.Create("new-workspace", "/repo", []);
        Assert.Equal(2, catalog.List().Count);
        Assert.NotEqual(healthy.Definition.Id, created.Definition.Id);
        var error = Assert.Throws<InvalidOperationException>(() => catalog.Resolve(broken.Definition.Id.ToString(), "/repo"));
        Assert.Contains(broken.ConfigurationPath, error.Message);
        Assert.Equal(invalid, fileSystem.File.ReadAllText(broken.ConfigurationPath));
    }

    [Fact]
    public void UnreadableSiblingDoesNotBlockHealthyListOrDirectResolution()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var healthy = catalog.Create("healthy", "/repo", []);
        var broken = catalog.Create("unreadable", "/repo", []);
        var streams = new Mock<IFileStreamFactory>();
        streams.Setup(value => value.New(It.IsAny<string>(), FileMode.Open, FileAccess.Read, It.IsAny<FileShare>()))
            .Returns((string path, FileMode mode, FileAccess access, FileShare share) =>
                path == broken.ConfigurationPath
                    ? throw new UnauthorizedAccessException("Access denied.")
                    : fileSystem.FileStream.New(path, mode, access, share));
        var wrapper = new Mock<IFileSystem>();
        wrapper.SetupGet(value => value.Path).Returns(fileSystem.Path);
        wrapper.SetupGet(value => value.File).Returns(fileSystem.File);
        wrapper.SetupGet(value => value.Directory).Returns(fileSystem.Directory);
        wrapper.SetupGet(value => value.DirectoryInfo).Returns(fileSystem.DirectoryInfo);
        wrapper.SetupGet(value => value.FileInfo).Returns(fileSystem.FileInfo);
        wrapper.SetupGet(value => value.FileStream).Returns(streams.Object);
        var isolated = new WorkspaceCatalog(wrapper.Object, catalog.HomeDirectory);

        Assert.Equal(healthy.Definition.Id, Assert.Single(isolated.List()).Definition.Id);
        Assert.Equal(healthy.Definition.Id, isolated.ResolveById(healthy.Definition.Id).Definition.Id);
        var error = Assert.Throws<InvalidOperationException>(() => isolated.ResolveById(broken.Definition.Id));
        Assert.Contains(broken.ConfigurationPath, error.Message);
        Assert.IsType<UnauthorizedAccessException>(error.InnerException);
    }

    [Fact]
    public void CreatingFromNestedDirectoryRebasesRelativeSourcesAndPreservesAbsoluteSelections()
    {
        var (fileSystem, catalog) = CreateCatalog();
        fileSystem.AddFile("/repo/frontend/docs/readme.md", new MockFileData("# Frontend"));

        var created = catalog.Create("frontend", "/repo/frontend", [
            new(WorkspaceSourceKind.TypeScript, "tsconfig.json"),
            new(WorkspaceSourceKind.Markdown, "docs/**/*.md"),
            new(WorkspaceSourceKind.CSharp, "/repo/backend/Orders.csproj")
        ]);

        Assert.Equal("/repo", created.Definition.RepositoryRoot);
        Assert.Equal(["frontend/tsconfig.json", "frontend/docs/**/*.md", "backend/Orders.csproj"],
            created.Definition.Sources.Select(source => source.Path));
        var updated = catalog.Update(created.Definition.Id.ToString(), "renamed", [new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json")]);
        Assert.Equal("frontend/tsconfig.json", Assert.Single(updated.Definition.Sources).Path);
        Assert.Equal(created.Definition.Id, updated.Definition.Id);
    }

    [Fact]
    public void RepositoryConfigIsIgnoredAndSourcesRoundTripThroughHome()
    {
        var (fileSystem, catalog) = CreateCatalog();
        fileSystem.AddFile("/repo/sharpsense.yaml", new MockFileData("invalid: [ yaml"));
        catalog.Create("product", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);

        var selected = catalog.Resolve(null, "/repo");

        Assert.Equal("docs/**/*.md", Assert.Single(selected.Definition.Sources).Path);
        Assert.Equal("invalid: [ yaml", fileSystem.File.ReadAllText("/repo/sharpsense.yaml"));
    }

    [Fact]
    public void ExistingLegacyDatabaseIsNeverModified()
    {
        var (fileSystem, catalog) = CreateCatalog();
        const string legacyDatabase = "/home/.SharpSense/legacy.db";
        fileSystem.AddFile(legacyDatabase, new MockFileData("legacy-data"));

        catalog.Create("product", "/repo", []);

        Assert.Equal("legacy-data", fileSystem.File.ReadAllText(legacyDatabase));
    }

    [Fact]
    public void UpdatingSourcesAndNamePreservesStorageIdentity()
    {
        var (_, catalog) = CreateCatalog();
        var created = catalog.Create("original", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);

        var updated = catalog.Update(created.Definition.Id.ToString(), "renamed",
            [new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json")]);

        Assert.Equal(created.Definition.Id, updated.Definition.Id);
        Assert.Equal(created.Workspace.DatabasePath, updated.Workspace.DatabasePath);
        Assert.Equal("renamed", catalog.Resolve(null, "/repo").Definition.Name);
        Assert.Equal(WorkspaceSourceKind.TypeScript, Assert.Single(updated.Definition.Sources).Kind);
    }

    [Fact]
    public void InvalidUpdatePreservesExistingDefinition()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var created = catalog.Create("original", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var originalYaml = fileSystem.File.ReadAllText(created.ConfigurationPath);

        Assert.Throws<ArgumentException>(() => catalog.Update("original", "new-name",
            [new(WorkspaceSourceKind.CSharp, "../outside.csproj")]));

        Assert.Equal(originalYaml, fileSystem.File.ReadAllText(created.ConfigurationPath));
        Assert.Equal("original", catalog.Resolve(null, "/repo").Definition.Name);
    }

    [Fact]
    public void BoundWorkspaceScopeCannotSwitchSelection()
    {
        var (_, catalog) = CreateCatalog();
        var first = catalog.Create("first", "/repo", []);
        var second = catalog.Create("second", "/repo", []);
        var scope = new WorkspaceScope();
        scope.Bind(first, watch: true, skipEmbeddings: true);

        Assert.Throws<InvalidOperationException>(() => scope.Bind(second));

        Assert.Equal(first.Definition.Id, scope.Selection.Definition.Id);
        Assert.True(scope.Watch);
        Assert.True(scope.SkipEmbeddings);
    }

    private static (MockFileSystem FileSystem, WorkspaceCatalog Catalog) CreateCatalog()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
            ["/repo/backend/Orders.csproj"] = new("<Project />"),
            ["/repo/frontend/tsconfig.json"] = new("{}")
        }, "/repo");

        return (fileSystem, new WorkspaceCatalog(fileSystem, "/home/sharpsense"));
    }
}
