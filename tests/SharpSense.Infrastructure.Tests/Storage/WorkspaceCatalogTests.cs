using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Storage;

public sealed class WorkspaceCatalogTests
{
    [Fact]
    public void WhenCreatingWorkspace_ThenPersistsHomeDefinitionWithoutCreatingDatabaseOrRepositoryConfig()
    {
        var (fileSystem, catalog) = CreateCatalog();

        var selection = catalog.Create(
            "product",
            "/repo",
            [
            new(WorkspaceSourceKind.CSharp, "backend/Orders.csproj"),
            new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json"),
            new(WorkspaceSourceKind.Markdown, "docs/**/*.md")
        ]);
        var loaded = catalog.Resolve("product");

        loaded.Definition.Id.Should().NotBe(Guid.Empty);
        loaded.Definition.Id.Should().Be(selection.Definition.Id);
        loaded.Workspace.RootPath.Should().Be("/repo");
        loaded.Workspace.DatabasePath.Should().Be($"/home/sharpsense/workspaces/{loaded.Definition.Id:D}/index.db");
        loaded.Definition.Sources.Length.Should().Be(3);
        loaded.Workspace.WorkspaceId.Should().Be(loaded.Definition.Id);
        loaded.Workspace.WorkspaceName.Should().Be("product");
        fileSystem.File.Exists(loaded.ConfigurationPath).Should().BeTrue();
        fileSystem.File.Exists(loaded.Workspace.DatabasePath).Should().BeFalse();
        fileSystem.File.Exists("/repo/sharpsense.yaml").Should().BeFalse();
        fileSystem.AllFiles.Should().NotContain(static path => path.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void WhenResolutionFromSubdirectory_ThenFindsUniqueRegisteredWorkspace()
    {
        var (_, catalog) = CreateCatalog();
        var expected = catalog.Create("product", "/repo", []);

        var selected = catalog.ResolveFromDirectory("/repo/frontend");

        selected.Definition.Id.Should().Be(expected.Definition.Id);
        catalog.Resolve(expected.Definition.Id.ToString()).Definition.Id.Should().Be(expected.Definition.Id);
    }

    [Fact]
    public void WhenMultipleWorkspaceSelection_ThenRequiresExplicitName()
    {
        var (_, catalog) = CreateCatalog();
        catalog.Create("frontend", "/repo", []);
        catalog.Create("backend", "/repo", []);

        var exception = ((Action)(() => catalog.ResolveFromDirectory("/repo/frontend"))).Should().ThrowExactly<InvalidOperationException>().Which;

        exception.Message.Should().Contain("--workspace");
        exception.Message.Should().Contain("frontend");
        exception.Message.Should().Contain("backend");
        catalog.Resolve("FRONTEND").Definition.Name.Should().Be("frontend");
    }

    [Fact]
    public void WhenMissingWorkspaceResolution_ThenDoesNotCreateStorage()
    {
        var (fileSystem, catalog) = CreateCatalog();

        catalog.List().Should().BeEmpty();
        var exception = ((Action)(() => catalog.ResolveFromDirectory("/repo/frontend"))).Should().ThrowExactly<InvalidOperationException>().Which;

        exception.Message.Should().Contain("sharpsense workspace create");
        fileSystem.Directory.Exists(catalog.HomeDirectory).Should().BeFalse();
    }

    [Fact]
    public void WhenWorkspacesInSameRepository_ThenHaveIndependentDatabasePaths()
    {
        var (_, catalog) = CreateCatalog();

        var frontend = catalog.Create("frontend", "/repo", []);
        var backend = catalog.Create("backend", "/repo", []);

        backend.Workspace.DatabasePath.Should().NotBe(frontend.Workspace.DatabasePath);
        backend.Workspace.RootPath.Should().Be(frontend.Workspace.RootPath);
    }

    [Fact]
    public void WhenMerging_ThenCopiesAndDeduplicatesSourcesWithoutSharingMutableDefinitions()
    {
        var (_, catalog) = CreateCatalog();
        WorkspaceSource docs = new(WorkspaceSourceKind.Markdown, "docs/**/*.md");
        WorkspaceSource frontendSource = new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json");
        catalog.Create("frontend", "/repo", [frontendSource, docs]);
        catalog.Create("backend", "/repo", [new(WorkspaceSourceKind.CSharp, "backend/Orders.csproj"), docs]);

        var merged = catalog.Merge("product", ["frontend", "backend"]);
        catalog.RemoveSources("frontend", [frontendSource]);

        merged.Definition.Sources.Length.Should().Be(3);
        merged.Definition.Sources.Should().ContainSingle(source => source == docs);
        catalog.Resolve("product").Definition.Sources.Should().Contain(frontendSource);
        catalog.Resolve("frontend").Definition.Sources.Should().NotContain(frontendSource);
    }

    [Fact]
    public void WhenMerge_ThenRejectsDifferentRepositoryRoots()
    {
        var (fileSystem, catalog) = CreateCatalog();
        fileSystem.AddDirectory("/other/.git");
        catalog.Create("first", "/repo", []);
        catalog.Create("second", "/other", []);

        var exception = ((Action)(() => catalog.Merge("combined", ["first", "second"]))).Should().ThrowExactly<InvalidOperationException>().Which;

        exception.Message.Should().Contain("same workspace root");
        catalog.List().Count.Should().Be(2);
    }

    [Fact]
    public void WhenAddAndRemoveNormalizePathsAnd_ThenPreserveWorkspaceIdentity()
    {
        var (_, catalog) = CreateCatalog();
        var created = catalog.Create("product", "/repo", []);

        var updated = catalog.AddSources(
            "product",
            [
            new(WorkspaceSourceKind.CSharp, "./backend/Orders.csproj"),
            new(WorkspaceSourceKind.CSharp, "/repo/backend/Orders.csproj")
        ]);
        var removed = catalog.RemoveSources("product", [new(WorkspaceSourceKind.CSharp, "backend/Orders.csproj")]);

        updated.Definition.Id.Should().Be(created.Definition.Id);
        removed.Selection.Definition.Id.Should().Be(created.Definition.Id);
        updated.Definition.Sources.Should().ContainSingle().Which.Path.Should().Be("backend/Orders.csproj");
        removed.Selection.Definition.Sources.Should().BeEmpty();
    }

    [Fact]
    public void WhenRemove_ThenReportsMatchedAndUnmatchedSourcesAndDoesNotRewriteOnNoMatch()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var source = new WorkspaceSource(WorkspaceSourceKind.CSharp, "backend/Orders.csproj");
        var missing = new WorkspaceSource(WorkspaceSourceKind.Markdown, "missing/**/*.md");
        var selection = catalog.Create("product", "/repo", [source]);
        var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        fileSystem.File.SetLastWriteTimeUtc(selection.ConfigurationPath, timestamp);

        var noMatch = catalog.RemoveSources("product", [missing]);

        noMatch.RemovedSources.Should().BeEmpty();
        noMatch.UnmatchedSources.Should().ContainSingle().Which.Should().Be(missing);
        noMatch.Selection.Definition.Sources.Should().ContainSingle().Which.Should().Be(source);
        fileSystem.File.GetLastWriteTimeUtc(selection.ConfigurationPath).Should().Be(timestamp);

        // Removal must still work after a configured file has been deleted.
        fileSystem.File.Delete("/repo/backend/Orders.csproj");
        var partial = catalog.RemoveSources("product", [source, source, missing]);

        partial.RemovedSources.Should().ContainSingle().Which.Should().Be(source);
        partial.UnmatchedSources.Should().ContainSingle().Which.Should().Be(missing);
        partial.Selection.Definition.Sources.Should().BeEmpty();
        catalog.ResolveById(selection.Definition.Id).Definition.Sources.Should().BeEmpty();
    }

    [Theory]
    [InlineData("../outside.csproj")]
    [InlineData("/elsewhere/outside.csproj")]
    [InlineData("backend/../../outside.csproj")]
    public void WhenSources_ThenCannotEscapeRegisteredRepository(string path)
    {
        var (fileSystem, catalog) = CreateCatalog();

        ((Action)(() => catalog.Create("product", "/repo", [new(WorkspaceSourceKind.CSharp, path)]))).Should().ThrowExactly<ArgumentException>();

        fileSystem.Directory.Exists(catalog.HomeDirectory).Should().BeFalse();
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("one/two")]
    [InlineData(".")]
    [InlineData("../../")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public void WhenWorkspaceNames_ThenCannotBePathsOrIds(string name)
    {
        var (_, catalog) = CreateCatalog();

        ((Action)(() => catalog.Create(name, "/repo", []))).Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void WhenDuplicateNames_ThenAreRejectedCaseInsensitively()
    {
        var (_, catalog) = CreateCatalog();
        catalog.Create("product", "/repo", []);

        ((Action)(() => catalog.Create("PRODUCT", "/repo", []))).Should().ThrowExactly<InvalidOperationException>();
        catalog.List().Should().ContainSingle();
    }

    [Fact]
    public void WhenDefinition_ThenCannotRedirectWorkspaceStorageThroughItsId()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var created = catalog.Create("product", "/repo", []);
        fileSystem.File.WriteAllText(
            created.ConfigurationPath,
            fileSystem.File.ReadAllText(created.ConfigurationPath)
                .Replace(
                created.Definition.Id.ToString(),
                Guid.NewGuid()
                    .ToString()));

        catalog.List().Should().BeEmpty();
        var exception = ((Action)(() => catalog.ResolveById(created.Definition.Id))).Should().ThrowExactly<InvalidOperationException>().Which;

        exception.Message.Should().Contain("ID must match");
    }

    [Fact]
    public void WhenUnsupportedConfigurationVersion_ThenIsActionable()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var created = catalog.Create("product", "/repo", []);
        fileSystem.File.WriteAllText(
            created.ConfigurationPath,
            fileSystem.File.ReadAllText(created.ConfigurationPath)
                .Replace("version: 1", "version: 999"));

        catalog.List().Should().BeEmpty();
        var exception = ((Action)(() => catalog.ResolveById(created.Definition.Id))).Should().ThrowExactly<InvalidOperationException>().Which;

        exception.Message.Should().Contain("unsupported");
        exception.Message.Should().Contain(created.ConfigurationPath);
    }

    [Fact]
    public void WhenMalformedSibling_ThenDoesNotBlockHealthyResolutionOrCreation()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var healthy = catalog.Create("healthy", "/repo", []);
        var broken = catalog.Create("broken", "/repo", []);
        const string invalid = "private: [ malformed configuration";
        fileSystem.File.WriteAllText(broken.ConfigurationPath, invalid);

        catalog.List().Should().ContainSingle().Which.Definition.Id.Should().Be(healthy.Definition.Id);
        catalog.Resolve("healthy").Definition.Id.Should().Be(healthy.Definition.Id);
        catalog.ResolveById(healthy.Definition.Id).Definition.Id.Should().Be(healthy.Definition.Id);
        catalog.ResolveFromDirectory("/repo").Definition.Id.Should().Be(healthy.Definition.Id);
        var created = catalog.Create("new-workspace", "/repo", []);
        catalog.List().Count.Should().Be(2);
        created.Definition.Id.Should().NotBe(healthy.Definition.Id);
        var error = ((Action)(() => catalog.Resolve(broken.Definition.Id.ToString()))).Should().ThrowExactly<InvalidOperationException>().Which;
        error.Message.Should().Contain(broken.ConfigurationPath);
        fileSystem.File.ReadAllText(broken.ConfigurationPath).Should().Be(invalid);
    }

    [Fact]
    public void WhenUnreadableSibling_ThenDoesNotBlockHealthyListOrDirectResolution()
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
        wrapper.SetupGet(value => value.Path)
            .Returns(fileSystem.Path);
        wrapper.SetupGet(value => value.File)
            .Returns(fileSystem.File);
        wrapper.SetupGet(value => value.Directory)
            .Returns(fileSystem.Directory);
        wrapper.SetupGet(value => value.DirectoryInfo)
            .Returns(fileSystem.DirectoryInfo);
        wrapper.SetupGet(value => value.FileInfo)
            .Returns(fileSystem.FileInfo);
        wrapper.SetupGet(value => value.FileStream)
            .Returns(streams.Object);
        var isolated = new WorkspaceCatalog(wrapper.Object, catalog.HomeDirectory);

        isolated.List().Should().ContainSingle().Which.Definition.Id.Should().Be(healthy.Definition.Id);
        isolated.ResolveById(healthy.Definition.Id).Definition.Id.Should().Be(healthy.Definition.Id);
        var error = ((Action)(() => isolated.ResolveById(broken.Definition.Id))).Should().ThrowExactly<InvalidOperationException>().Which;
        error.Message.Should().Contain(broken.ConfigurationPath);
        error.InnerException.Should().BeOfType<UnauthorizedAccessException>();
    }

    [Fact]
    public void WhenCreatingFromNestedDirectory_ThenPreservesTheChosenRootAndNormalizesSources()
    {
        var (fileSystem, catalog) = CreateCatalog();
        fileSystem.AddFile("/repo/frontend/docs/readme.md", new MockFileData("# Frontend"));

        var created = catalog.Create(
            "frontend",
            "/repo/frontend",
            [
            new(WorkspaceSourceKind.TypeScript, "tsconfig.json"),
            new(WorkspaceSourceKind.Markdown, "docs/**/*.md"),
            new(WorkspaceSourceKind.Markdown, "/repo/frontend/docs/readme.md")
        ]);

        created.Definition.WorkspaceRoot.Should().Be("/repo/frontend");
        created.Definition.Sources.Select(source => source.Path).Should().Equal(["tsconfig.json", "docs/**/*.md", "docs/readme.md"]);
        var updated = catalog.Update(
            created.Definition.Id.ToString(),
            "renamed",
            [new(WorkspaceSourceKind.TypeScript, "tsconfig.json")]);
        updated.Definition.Sources.Should().ContainSingle().Which.Path.Should().Be("tsconfig.json");
        updated.Definition.Id.Should().Be(created.Definition.Id);
    }

    [Fact]
    public void WhenRepositoryConfig_ThenIsIgnoredAndSourcesRoundTripThroughHome()
    {
        var (fileSystem, catalog) = CreateCatalog();
        fileSystem.AddFile("/repo/sharpsense.yaml", new MockFileData("invalid: [ yaml"));
        catalog.Create("product", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);

        var selected = catalog.Resolve("product");

        selected.Definition.Sources.Should().ContainSingle().Which.Path.Should().Be("docs/**/*.md");
        fileSystem.File.ReadAllText("/repo/sharpsense.yaml").Should().Be("invalid: [ yaml");
    }

    [Fact]
    public void WhenExistingLegacyDatabase_ThenIsNeverModified()
    {
        var (fileSystem, catalog) = CreateCatalog();
        const string legacyDatabase = "/home/.SharpSense/legacy.db";
        fileSystem.AddFile(legacyDatabase, new MockFileData("legacy-data"));

        catalog.Create("product", "/repo", []);

        fileSystem.File.ReadAllText(legacyDatabase).Should().Be("legacy-data");
    }

    [Fact]
    public void WhenUpdatingSourcesAndName_ThenPreservesStorageIdentity()
    {
        var (_, catalog) = CreateCatalog();
        var created = catalog.Create("original", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);

        var updated = catalog.Update(
            created.Definition.Id.ToString(),
            "renamed",
            [new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json")]);

        updated.Definition.Id.Should().Be(created.Definition.Id);
        updated.Workspace.DatabasePath.Should().Be(created.Workspace.DatabasePath);
        catalog.Resolve("renamed").Definition.Name.Should().Be("renamed");
        updated.Definition.Sources.Should().ContainSingle().Which.Kind.Should().Be(WorkspaceSourceKind.TypeScript);
    }

    [Fact]
    public void WhenInvalidUpdate_ThenPreservesExistingDefinition()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var created = catalog.Create("original", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var originalYaml = fileSystem.File.ReadAllText(created.ConfigurationPath);

        ((Action)(() => catalog.Update(
            "original",
            "new-name",
            [new(WorkspaceSourceKind.CSharp, "../outside.csproj")]))).Should().ThrowExactly<ArgumentException>();

        fileSystem.File.ReadAllText(created.ConfigurationPath).Should().Be(originalYaml);
        catalog.Resolve("original").Definition.Name.Should().Be("original");
    }

    [Fact]
    public void WhenDefault_ThenIsGlobalPersistsByIdAndExplicitSelectionOverridesIt()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var first = catalog.Create("first", "/repo", []);
        var second = catalog.Create("second", "/repo", []);
        fileSystem.AddDirectory("/other/.git");
        var other = catalog.Create("other", "/other", []);

        catalog.Use("second");
        var reloaded = new WorkspaceCatalog(fileSystem, catalog.HomeDirectory);

        reloaded.Resolve(null).Definition.Id.Should().Be(second.Definition.Id);
        reloaded.Resolve("first").Definition.Id.Should().Be(first.Definition.Id);
        reloaded.Resolve(other.Definition.Id.ToString()).Definition.Id.Should().Be(other.Definition.Id);
        fileSystem.File.ReadAllText("/home/sharpsense/default-workspace").Should().Be(second.Definition.Id.ToString("D"));
        fileSystem.File.Exists(second.Workspace.DatabasePath).Should().BeFalse();
        fileSystem.AllFiles.Should().NotContain(path => path.EndsWith(".tmp", StringComparison.Ordinal));

        catalog.Rename("second", "renamed");
        reloaded.Resolve(null).Definition.Name.Should().Be("renamed");

        ((Action)(() => catalog.Use("missing"))).Should().ThrowExactly<InvalidOperationException>();
        catalog.GetDefaultWorkspaceId().Should().Be(second.Definition.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WhenExplicitBlankSelectors_ThenNeverFallBackToRepositoryOrDefaultSelection(string selector)
    {
        var (_, catalog) = CreateCatalog();
        catalog.Create("first", "/repo", []);

        ((Action)(() => catalog.Resolve(selector))).Should().Throw<ArgumentException>();

        catalog.Use("first");

        ((Action)(() => catalog.Resolve(selector))).Should().Throw<ArgumentException>();
        catalog.Resolve(null).Definition.Name.Should().Be("first");
    }

    [Fact]
    public void WhenDefaultChangesDuringIndexing_ThenExistingSelectionRemainsBound()
    {
        var (_, catalog) = CreateCatalog();
        var first = catalog.Create("first", "/repo", []);
        var second = catalog.Create("second", "/repo", []);
        catalog.Use("first");
        var bound = catalog.Resolve(null);

        using var lease = catalog.AcquireIndexLease(first);
        catalog.Use("second");

        bound.Definition.Id.Should().Be(first.Definition.Id);
        catalog.Resolve(null).Definition.Id.Should().Be(second.Definition.Id);
    }

    [Fact]
    public void WhenUnavailableDefault_ThenDoesNotSilentlySelectADifferentWorkspace()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var first = catalog.Create("first", "/repo", []);
        var second = catalog.Create("second", "/repo", []);
        catalog.Use("first");
        fileSystem.File.Delete(first.ConfigurationPath);

        var missing = ((Action)(() => catalog.Resolve(null))).Should().ThrowExactly<InvalidOperationException>().Which;
        missing.Message.Should().Contain("workspace use");
        catalog.Resolve("second").Definition.Id.Should().Be(second.Definition.Id);

        fileSystem.File.WriteAllText("/home/sharpsense/default-workspace", "invalid");
        var invalid = ((Action)(() => catalog.Resolve(null))).Should().ThrowExactly<InvalidOperationException>().Which;
        invalid.Message.Should().Contain("workspace use");
        catalog.Resolve("second").Definition.Id.Should().Be(second.Definition.Id);

        catalog.Use("second");
        catalog.Resolve(null).Definition.Id.Should().Be(second.Definition.Id);
    }

    [Fact]
    public void WhenDefaultSelections_ThenAreIsolatedBySharpSenseHome()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var first = catalog.Create("first", "/repo", []);
        catalog.Use("first");
        var other = new WorkspaceCatalog(fileSystem, "/different-home");

        other.GetDefaultWorkspaceId().Should().BeNull();
        fileSystem.Directory.Exists(other.HomeDirectory).Should().BeFalse();
        ((Action)(() => other.Resolve(null))).Should().ThrowExactly<InvalidOperationException>();
    }

    [Fact]
    public void WhenBoundWorkspaceScope_ThenCannotSwitchSelection()
    {
        var (_, catalog) = CreateCatalog();
        var first = catalog.Create("first", "/repo", []);
        var second = catalog.Create("second", "/repo", []);
        var scope = new WorkspaceScope();
        scope.Bind(first, skipEmbeddings: true);

        ((Action)(() => scope.Bind(second))).Should().ThrowExactly<InvalidOperationException>();

        scope.Selection.Definition.Id.Should().Be(first.Definition.Id);
        scope.SkipEmbeddings.Should().BeTrue();
    }

    [Fact]
    public void WhenCliHasNoSavedDefault_ThenAUniqueDirectoryMatchDoesNotSelectAWorkspace()
    {
        var (_, catalog) = CreateCatalog();
        catalog.Create("product", "/repo", []);

        var error = ((Action)(() => catalog.Resolve(null))).Should().ThrowExactly<InvalidOperationException>().Which;

        error.Message.Should().Contain("--workspace");
        error.Message.Should().Contain("workspace use");
    }

    [Theory]
    [InlineData("/repo")]
    [InlineData("/repo/frontend")]
    [InlineData("/repo/frontend/.")]
    public void WhenDirectoryDiscoveryHasADefaultElsewhere_ThenItSelectsTheMatchingWorkspace(string directory)
    {
        var (fileSystem, catalog) = CreateCatalog();
        var expected = catalog.Create("product", "/repo", []);
        fileSystem.AddDirectory("/other");
        catalog.Create("elsewhere", "/other", []);
        catalog.Use("elsewhere");
        fileSystem.AddDirectory("/repo/frontend/.git");

        var selection = catalog.ResolveFromDirectory(directory);

        selection.Definition.Id.Should().Be(expected.Definition.Id);
        catalog.Resolve(null).Definition.Name.Should().Be("elsewhere");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    public void WhenDirectoryDiscoveryHasAnUnavailableDefault_ThenItNeverReadsThatDefault(string savedDefault)
    {
        var (fileSystem, catalog) = CreateCatalog();
        var expected = catalog.Create("product", "/repo", []);
        fileSystem.File.WriteAllText("/home/sharpsense/default-workspace", savedDefault);

        var selection = catalog.ResolveFromDirectory("/repo/backend");

        selection.Definition.Id.Should().Be(expected.Definition.Id);
        fileSystem.File.ReadAllText("/home/sharpsense/default-workspace").Should().Be(savedDefault);
    }

    [Theory]
    [InlineData("/repo-other")]
    [InlineData("/unrelated")]
    public void WhenDirectoryDoesNotMatch_ThenTheSavedDefaultCannotRescueDiscovery(string directory)
    {
        var (_, catalog) = CreateCatalog();
        catalog.Create("product", "/repo", []);
        catalog.Use("product");

        var error = ((Action)(() => catalog.ResolveFromDirectory(directory))).Should().ThrowExactly<InvalidOperationException>().Which;

        error.Message.Should().Contain("No workspace matches");
        error.Message.Should().Contain("--workspace");
    }

    [Fact]
    public void WhenNestedWorkspaceRootsBothMatch_ThenDiscoveryRequiresAnExplicitSelection()
    {
        var (_, catalog) = CreateCatalog();
        catalog.Create("product", "/repo", []);
        catalog.Create("frontend", "/repo/frontend", []);
        catalog.Use("frontend");

        var error = ((Action)(() => catalog.ResolveFromDirectory("/repo/frontend"))).Should().ThrowExactly<InvalidOperationException>().Which;

        error.Message.Should().Contain("product");
        error.Message.Should().Contain("frontend");
        error.Message.Should().Contain("--workspace");
    }

    [Fact]
    public void WhenSourcesSpanRepositories_ThenTheyRemainRelativeToTheConfiguredRoot()
    {
        var (fileSystem, catalog) = CreateCatalog();
        fileSystem.AddDirectory("/repo/backend/.git");
        fileSystem.AddDirectory("/repo/frontend/.git");

        var created = catalog.Create("product", "/repo", [
            new(WorkspaceSourceKind.CSharp, "backend/Orders.csproj"),
            new(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json")]);

        created.Definition.WorkspaceRoot.Should().Be("/repo");
        created.Definition.Sources.Select(source => source.Path).Should().Equal("backend/Orders.csproj", "frontend/tsconfig.json");
        catalog.ResolveFromDirectory("/repo/backend").Definition.Id.Should().Be(created.Definition.Id);
    }

    [Fact]
    public void WhenLegacyRootIsLoadedAndSaved_ThenOnlyTheYamlKeyChanges()
    {
        var (fileSystem, catalog) = CreateCatalog();
        var created = catalog.Create("product", "/repo", [new(WorkspaceSourceKind.CSharp, "backend/Orders.csproj")]);
        var legacyYaml = fileSystem.File.ReadAllText(created.ConfigurationPath).Replace("workspaceRoot:", "repositoryRoot:");
        fileSystem.File.WriteAllText(created.ConfigurationPath, legacyYaml);
        fileSystem.File.WriteAllText(created.Workspace.DatabasePath, "existing graph and memories");

        var loaded = catalog.ResolveById(created.Definition.Id);

        loaded.Definition.WorkspaceRoot.Should().Be("/repo");
        fileSystem.File.ReadAllText(created.ConfigurationPath).Should().Be(legacyYaml);

        var saved = catalog.Rename("product", "renamed");
        var yaml = fileSystem.File.ReadAllText(saved.ConfigurationPath);

        yaml.Should().Contain("workspaceRoot: /repo");
        yaml.Should().NotContain("repositoryRoot:");
        saved.Definition.Id.Should().Be(created.Definition.Id);
        saved.Definition.Version.Should().Be(1);
        saved.Definition.Sources.Should().Equal(created.Definition.Sources);
        saved.Workspace.DatabasePath.Should().Be(created.Workspace.DatabasePath);
        fileSystem.File.ReadAllText(saved.Workspace.DatabasePath).Should().Be("existing graph and memories");
    }

    [Theory]
    [InlineData("/repo", true)]
    [InlineData("/repo/", true)]
    [InlineData("/other", false)]
    [InlineData("relative", false)]
    public void WhenBothRootKeysArePresent_ThenTheyMustIdentifyTheSameAbsoluteDirectory(string legacyRoot, bool valid)
    {
        var (fileSystem, catalog) = CreateCatalog();
        var created = catalog.Create("product", "/repo", []);
        var yaml = fileSystem.File.ReadAllText(created.ConfigurationPath) + $"repositoryRoot: {legacyRoot}\n";
        fileSystem.File.WriteAllText(created.ConfigurationPath, yaml);

        if (valid)
        {
            catalog.ResolveById(created.Definition.Id).Definition.WorkspaceRoot.Should().Be("/repo");
        }
        else
        {
            var error = ((Action)(() => catalog.ResolveById(created.Definition.Id))).Should().ThrowExactly<InvalidOperationException>().Which;
            error.Message.Should().Contain("workspaceRoot and legacy repositoryRoot");
        }

        fileSystem.File.ReadAllText(created.ConfigurationPath).Should().Be(yaml);
    }

    private static (MockFileSystem FileSystem, WorkspaceCatalog Catalog) CreateCatalog()
    {
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/backend/Orders.csproj"] = new("<Project />"),
                ["/repo/frontend/tsconfig.json"] = new("{}")
            },
            "/repo");

        return (fileSystem, new WorkspaceCatalog(fileSystem, "/home/sharpsense"));
    }
}
