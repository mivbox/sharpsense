using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceCommandTests
{
    [Fact]
    public async Task Configure_CreatesOneNamedCompositeWorkspaceWithoutCreatingDatabaseOrProjectConfiguration()
    {
        var fixture = new Fixture();
        var result = await fixture.Run("configure", "product", "--repo-root", "/repo",
            "--csharp", "src/Api/Api.csproj", "--csharp", "src/Core/Core.csproj", "--csharp", "src/Jobs/Jobs.csproj",
            "--typescript", "frontend/tsconfig.json", "--markdown", "docs/**/*.md", "--json");

        result.ExitCode.Should().Be(0, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        document.RootElement.GetProperty("name").GetString().Should().Be("product");
        document.RootElement.GetProperty("sources").GetArrayLength().Should().Be(5);
        var selection = fixture.Catalog.Resolve("product", "/repo");
        fixture.FileSystem.File.Exists(selection.ConfigurationPath).Should().BeTrue();
        fixture.FileSystem.File.Exists(selection.Workspace.DatabasePath).Should().BeFalse();
        fixture.FileSystem.File.Exists("/repo/sharpsense.yaml").Should().BeFalse();
    }

    [Fact]
    public async Task WorkspaceCommands_AddRemoveAndMergeSourcesWithoutChangingExistingDatabases()
    {
        var fixture = new Fixture();
        (await fixture.Run("workspace", "create", "backend", "--repo-root", "/repo", "--csharp", "src/Api/Api.csproj")).ExitCode.Should().Be(0);
        (await fixture.Run("workspace", "create", "frontend", "--repo-root", "/repo", "--typescript", "frontend/tsconfig.json")).ExitCode.Should().Be(0);
        var backend = fixture.Catalog.Resolve("backend", "/repo");
        fixture.FileSystem.AddFile(backend.Workspace.DatabasePath, new MockFileData("preserved database"));

        (await fixture.Run("workspace", "add", "backend", "--markdown", "docs/**/*.md")).ExitCode.Should().Be(0);
        (await fixture.Run("workspace", "remove", "backend", "--markdown", "docs/**/*.md")).ExitCode.Should().Be(0);
        var merged = await fixture.Run("workspace", "merge", "product", "backend", "frontend", "--json");

        merged.ExitCode.Should().Be(0, merged.Output);
        var product = fixture.Catalog.Resolve("product", "/repo");
        product.Definition.Sources.Should().HaveCount(2);
        product.Definition.Id.Should().NotBe(backend.Definition.Id);
        fixture.Catalog.List().Should().HaveCount(3);
        fixture.Catalog.Resolve("backend", "/repo").Definition.Sources.Should().ContainSingle();
        fixture.FileSystem.File.ReadAllText(backend.Workspace.DatabasePath).Should().Be("preserved database");
    }

    [Fact]
    public async Task WorkspaceCreate_ResolvesRelativeSourcesBeforeCanonicalizingGitRoot()
    {
        var fixture = new Fixture();

        var result = await fixture.Run("workspace", "create", "api", "--repo-root", "/repo/src/Api", "--csharp", "Api.csproj");

        result.ExitCode.Should().Be(0, result.Output);
        var workspace = fixture.Catalog.Resolve("api", "/repo");
        workspace.Definition.RepositoryRoot.Should().Be("/repo");
        workspace.Definition.Sources.Should().ContainSingle(source => source.Path == "src/Api/Api.csproj");
    }

    [Fact]
    public async Task Analyze_RejectsLegacyPositionalTargetAndExplainsMissingWorkspace()
    {
        var fixture = new Fixture();

        var legacy = await fixture.Run("analyze", "src/Api/Api.csproj", "--repo-root", "/repo");
        var unregistered = await fixture.Run("analyze", "--repo-root", "/repo", "--no-embeddings");

        legacy.ExitCode.Should().NotBe(0);
        unregistered.ExitCode.Should().Be(1);
        unregistered.Output.Should().Contain("No workspace is registered");
        fixture.Catalog.List().Should().BeEmpty();
    }

    [Fact]
    public async Task MalformedSiblingDoesNotPolluteWorkspaceJsonOutput()
    {
        var fixture = new Fixture();
        var healthy = fixture.Catalog.Create("healthy", "/repo", []);
        var broken = fixture.Catalog.Create("broken", "/repo", []);
        fixture.FileSystem.File.WriteAllText(broken.ConfigurationPath, "private: [ malformed yaml");

        var listed = await fixture.Run("workspace", "list", "--json");
        var selected = await fixture.Run("workspace", "show", "--workspace", healthy.Definition.Id.ToString(), "--json");

        Assert.Equal(0, listed.ExitCode);
        Assert.Equal(0, selected.ExitCode);
        using var list = JsonDocument.Parse(listed.Output);
        using var detail = JsonDocument.Parse(selected.Output);
        Assert.Equal("healthy", Assert.Single(list.RootElement.EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal("healthy", detail.RootElement.GetProperty("name").GetString());
        Assert.DoesNotContain("Skipping", listed.Output);
        Assert.DoesNotContain("private", listed.Output);
    }

    [Fact]
    public async Task WorkspaceList_IsReadOnlyAndExplicitSelectionResolvesAmbiguity()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("backend", "/repo", [new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj")]);
        fixture.Catalog.Create("frontend", "/repo", [new WorkspaceSource(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json")]);
        var pathsBefore = fixture.FileSystem.AllFiles.Order().ToArray();

        var listed = await fixture.Run("workspace", "list", "--json");
        var ambiguous = await fixture.Run("workspace", "show", "--repo-root", "/repo");
        var selected = await fixture.Run("workspace", "show", "--workspace", "frontend", "--json");

        listed.ExitCode.Should().Be(0);
        ambiguous.ExitCode.Should().Be(1);
        ambiguous.Output.Should().Contain("Multiple workspaces match");
        selected.ExitCode.Should().Be(0, selected.Output);
        using var document = JsonDocument.Parse(selected.Output);
        document.RootElement.GetProperty("name").GetString().Should().Be("frontend");
        fixture.FileSystem.AllFiles.Order().Should().Equal(pathsBefore);
    }

    private sealed class Fixture
    {
        public MockFileSystem FileSystem { get; } = new(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
            ["/repo/src/Api/Api.csproj"] = new("<Project />"),
            ["/repo/src/Core/Core.csproj"] = new("<Project />"),
            ["/repo/src/Jobs/Jobs.csproj"] = new("<Project />"),
            ["/repo/frontend/tsconfig.json"] = new("{}"),
            ["/repo/docs/guide.md"] = new("# Guide")
        }, "/repo");

        public WorkspaceCatalog Catalog { get; }

        public Fixture() => Catalog = new WorkspaceCatalog(FileSystem, "/test-home/.sharpsense");

        public async Task<(int ExitCode, string Output)> Run(params string[] args)
        {
            using var console = new TestConsole();
            var app = Cli.Program.CreateCommandApp(console, services =>
            {
                services.AddSingleton<IFileSystem>(FileSystem);
                services.AddSingleton(Catalog);
            }, enableFileLogging: false);
            var exitCode = await app.RunAsync(args, TestContext.Current.CancellationToken);
            return (exitCode, console.Output);
        }
    }
}
