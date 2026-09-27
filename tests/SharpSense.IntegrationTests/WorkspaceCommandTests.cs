using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Mcp;
using SharpSense.Cli.Shared;
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

        (await fixture.Run("workspace", "add", "backend", "--repo-root", "/repo", "--markdown", "docs/**/*.md")).ExitCode.Should().Be(0);
        (await fixture.Run("workspace", "remove", "backend", "--repo-root", "/repo", "--markdown", "docs/**/*.md")).ExitCode.Should().Be(0);
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

    [Fact]
    public async Task UsePersistsDefaultAndLsMarksItWhileShowCanOverrideIt()
    {
        var fixture = new Fixture();
        var first = fixture.Catalog.Create("first", "/repo", []);
        var second = fixture.Catalog.Create("second", "/repo", []);

        var used = await fixture.Run("workspace", "use", "second", "--json");
        var shown = await fixture.Run("workspace", "show", "--repo-root", "/elsewhere", "--json");
        var overridden = await fixture.Run("workspace", "show", "--workspace", "first", "--json");
        var listed = await fixture.Run("workspace", "ls", "--json");

        Assert.Equal(0, used.ExitCode);
        Assert.Equal(0, shown.ExitCode);
        Assert.Equal(0, overridden.ExitCode);
        Assert.Equal(0, listed.ExitCode);
        using var defaultJson = JsonDocument.Parse(shown.Output);
        using var overrideJson = JsonDocument.Parse(overridden.Output);
        using var listJson = JsonDocument.Parse(listed.Output);
        Assert.Equal(second.Definition.Id, defaultJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(first.Definition.Id, overrideJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(second.Definition.Id, Assert.Single(listJson.RootElement.EnumerateArray(),
            item => item.GetProperty("isDefault").GetBoolean()).GetProperty("id").GetGuid());
        Assert.Contains("(default)", (await fixture.Run("workspace", "ls")).Output);

        var noExplicitMcp = await fixture.Run("mcp");
        Assert.NotEqual(0, noExplicitMcp.ExitCode);
        Assert.Contains("MCP requires --workspace", noExplicitMcp.Output);
        Assert.Equal(second.Definition.Id, fixture.Catalog.GetDefaultWorkspaceId());
        Assert.False(fixture.FileSystem.File.Exists(second.Workspace.DatabasePath));
    }

    [Fact]
    public void ExplicitMcpWorkspaceBindingsRemainIndependentOfDefaultChanges()
    {
        var fixture = new Fixture();
        var first = fixture.Catalog.Create("first", "/repo", []);
        var second = fixture.Catalog.Create("second", "/repo", []);
        fixture.Catalog.Use("first");

        using var firstHost = Bind("first");
        using var secondHost = Bind("second");
        var firstSelection = firstHost.GetRequiredService<WorkspaceSelection>();
        var secondSelection = secondHost.GetRequiredService<WorkspaceSelection>();
        fixture.Catalog.Use("second");

        Assert.Equal(first.Definition.Id, firstSelection.Definition.Id);
        Assert.Equal(second.Definition.Id, secondSelection.Definition.Id);
        Assert.Same(firstSelection, firstHost.GetRequiredService<WorkspaceSelection>());
        Assert.Same(secondSelection, secondHost.GetRequiredService<WorkspaceSelection>());

        ServiceProvider Bind(string name)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IFileSystem>(fixture.FileSystem);
            services.AddSingleton(fixture.Catalog);
            services.AddSelectedWorkspace(new McpCommand.Settings { Workspace = name });
            return services.BuildServiceProvider();
        }
    }

    [Fact]
    public async Task LsUsesTheSameJsonOutputAsList()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("docs", "/repo", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);

        var listed = await fixture.Run("workspace", "list", "--json");
        var alias = await fixture.Run("workspace", "ls", "--json");

        Assert.Equal(0, alias.ExitCode);
        Assert.Equal(listed.Output, alias.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceCommandsUseActualWorkingDirectoryDespiteStalePwd(bool useExplicitBase)
    {
        var fixture = new Fixture();
        var previousDirectory = Environment.CurrentDirectory;
        var previousPwd = Environment.GetEnvironmentVariable("PWD");
        var temporaryDirectory = Directory.CreateTempSubdirectory("sharpsense-path-tests-");

        try
        {
            Environment.CurrentDirectory = temporaryDirectory.FullName;
            var taskRoot = Environment.CurrentDirectory;
            var repositoryRoot = Path.Combine(taskRoot, "repo");
            var sourceDirectory = Path.Combine(repositoryRoot, "src", "Api");
            Directory.CreateDirectory(sourceDirectory);
            fixture.FileSystem.AddFile(Path.Combine(repositoryRoot, ".git", "HEAD"), new MockFileData("ref: refs/heads/main"));
            fixture.FileSystem.AddFile(Path.Combine(sourceDirectory, "Api.csproj"), new MockFileData("<Project />"));
            fixture.FileSystem.AddFile(Path.Combine(repositoryRoot, "src", "Core", "Core.csproj"), new MockFileData("<Project />"));

            Environment.CurrentDirectory = useExplicitBase ? taskRoot : sourceDirectory;
            Environment.SetEnvironmentVariable("PWD", repositoryRoot);
            string[] rootOptions = useExplicitBase ? ["--repo-root", "repo/src/Api"] : [];

            var created = await fixture.Run(["workspace", "create", "api", .. rootOptions,
                "--csharp", "Api.csproj", "--markdown", "docs/*.md", "--json"]);
            Assert.Equal(0, created.ExitCode);
            fixture.Catalog.AddSources("api", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/*.md")]);

            var added = await fixture.Run(["workspace", "add", "api", .. rootOptions, "--csharp", "../Core/Core.csproj", "--json"]);

            Assert.Equal(0, added.ExitCode);
            var sources = fixture.Catalog.Resolve("api", repositoryRoot).Definition.Sources;
            Assert.Equal(4, sources.Length);
            Assert.Contains(new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj"), sources);
            Assert.Contains(new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Core/Core.csproj"), sources);
            Assert.Contains(new WorkspaceSource(WorkspaceSourceKind.Markdown, "src/Api/docs/*.md"), sources);

            var removed = await fixture.Run(["workspace", "remove", "api", .. rootOptions,
                "--csharp", "../Core/Core.csproj", "--markdown", "docs/*.md", "--markdown", "missing/**/*.md", "--json"]);

            Assert.Equal(0, removed.ExitCode);
            using var document = JsonDocument.Parse(removed.Output);
            Assert.Equal("api", document.RootElement.GetProperty("name").GetString());
            Assert.Equal(2, document.RootElement.GetProperty("removedSources").GetArrayLength());
            Assert.Equal("src/Api/missing/**/*.md", Assert.Single(document.RootElement.GetProperty("unmatchedSources").EnumerateArray()).GetProperty("path").GetString());
            var remaining = fixture.Catalog.Resolve("api", repositoryRoot).Definition.Sources;
            Assert.Equal(2, remaining.Length);
            Assert.Contains(new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj"), remaining);
            Assert.Contains(new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/*.md"), remaining);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Environment.SetEnvironmentVariable("PWD", previousPwd);
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveReportsNoMatchingSourcesWithoutClaimingAnUpdate(bool json)
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("docs", "/repo", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        string[] outputOptions = json ? ["--json"] : [];

        var result = await fixture.Run(["workspace", "remove", "docs", "--repo-root", "/repo",
            "--markdown", "typo/**/*.md", .. outputOptions]);

        Assert.Equal(0, result.ExitCode);
        Assert.Single(fixture.Catalog.Resolve("docs", "/repo").Definition.Sources);
        if (json)
        {
            using var document = JsonDocument.Parse(result.Output);
            Assert.Empty(document.RootElement.GetProperty("removedSources").EnumerateArray());
            Assert.Equal("typo/**/*.md", Assert.Single(document.RootElement.GetProperty("unmatchedSources").EnumerateArray()).GetProperty("path").GetString());
        }
        else
        {
            Assert.Contains("Removed 0 source(s)", result.Output);
            Assert.Contains("Not registered Markdown: typo/**/*.md", result.Output);
            Assert.DoesNotContain("Updated workspace", result.Output);
        }
    }

    [Theory]
    [InlineData("list", false)]
    [InlineData("ls", false)]
    [InlineData("use", false)]
    [InlineData("rename", false)]
    [InlineData("merge", false)]
    [InlineData("create", true)]
    [InlineData("add", true)]
    [InlineData("remove", true)]
    public async Task CatalogHelpOnlyAdvertisesMeaningfulOptions(string command, bool hasSourceBase)
    {
        var result = await new Fixture().Run("workspace", command, "--help");

        Assert.Equal(0, result.ExitCode);
        var options = result.Output[result.Output.IndexOf("OPTIONS:", StringComparison.Ordinal)..];
        Assert.DoesNotContain("--workspace", options);
        Assert.Equal(hasSourceBase, options.Contains("--repo-root", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IgnoredWorkspaceFlagsAreRejectedAndShowRejectsConflictingSelectors()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("docs", "/repo", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);

        var unsupported = await fixture.Run("workspace", "add", "docs", "--workspace", "other", "--repo-root", "/repo", "--markdown", "more/**/*.md");
        var conflicting = await fixture.Run("workspace", "show", "docs", "--workspace", "other");

        Assert.NotEqual(0, unsupported.ExitCode);
        Assert.Contains("Unknown option", unsupported.Output);
        Assert.NotEqual(0, conflicting.ExitCode);
        Assert.Contains("either a workspace argument or --workspace", conflicting.Output);
        Assert.Single(fixture.Catalog.Resolve("docs", "/repo").Definition.Sources);
    }

    [Theory]
    [InlineData("list", false)]
    [InlineData("ls", false)]
    [InlineData("list", true)]
    [InlineData("ls", true)]
    public async Task ListingWithMalformedDefaultPreservesOutputAndWarnsOnStderr(string command, bool json)
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("first", "/repo", []);
        fixture.Catalog.Create("second", "/repo", []);
        fixture.Catalog.Use("first");
        var defaultPath = Path.Combine(fixture.Catalog.HomeDirectory, "default-workspace");
        fixture.FileSystem.File.WriteAllText(defaultPath, "invalid");
        var filesBefore = fixture.FileSystem.AllFiles.Order().ToArray();
        var previousError = Console.Error;
        using var errors = new StringWriter();
        string[] options = json ? ["--json"] : [];

        try
        {
            Console.SetError(errors);
            var result = await fixture.Run(["workspace", command, .. options]);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Warning:", errors.ToString());
            Assert.Contains("workspace use", errors.ToString());
            Assert.DoesNotContain("Warning:", result.Output);
            if (json)
            {
                using var document = JsonDocument.Parse(result.Output);
                Assert.Equal(2, document.RootElement.GetArrayLength());
                Assert.All(document.RootElement.EnumerateArray(), item => Assert.False(item.GetProperty("isDefault").GetBoolean()));
            }
            else
            {
                Assert.Contains("first", result.Output);
                Assert.Contains("second", result.Output);
                Assert.DoesNotContain("(default)", result.Output);
            }

            Assert.Equal("invalid", fixture.FileSystem.File.ReadAllText(defaultPath));
            Assert.Equal(filesBefore, fixture.FileSystem.AllFiles.Order());
        }
        finally
        {
            Console.SetError(previousError);
        }

        Assert.Equal(0, (await fixture.Run("workspace", "use", "second")).ExitCode);
        Assert.Equal("second", fixture.Catalog.Resolve(null, "/elsewhere").Definition.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankShowArgumentCannotFallBackToTheDefaultOrMaskAnExplicitWorkspace(string selector)
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("first", "/repo", []);
        fixture.Catalog.Create("second", "/repo", []);
        fixture.Catalog.Use("first");

        var blank = await fixture.Run("workspace", "show", selector, "--json");
        var conflict = await fixture.Run("workspace", "show", selector, "--workspace", "second", "--json");

        Assert.NotEqual(0, blank.ExitCode);
        Assert.Contains("must not be empty", blank.Output);
        Assert.NotEqual(0, conflict.ExitCode);
        Assert.Contains("either a workspace argument or --workspace", conflict.Output);
        Assert.Equal("first", fixture.Catalog.Resolve(null, "/repo").Definition.Name);
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
