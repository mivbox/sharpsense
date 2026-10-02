using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Mcp;
using SharpSense.Cli.Shared;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Testing;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceCommandTests
{
    [Fact]
    public async Task WhenConfiguring_ThenCreatesCompositeWorkspaceWithoutDatabaseOrProjectConfiguration()
    {
        var fixture = new Fixture();
        var result = await fixture.Run(
            TestContext.Current.CancellationToken,
            "configure",
            "product",
            "--repo-root",
            "/repo",
            "--csharp",
            "src/Api/Api.csproj",
            "--csharp",
            "src/Core/Core.csproj",
            "--csharp",
            "src/Jobs/Jobs.csproj",
            "--typescript",
            "frontend/tsconfig.json",
            "--markdown",
            "docs/**/*.md",
            "--json");

        result.ExitCode.Should().Be(0, result.Output);
        using var document = JsonDocument.Parse(result.Output);
        document.RootElement.GetProperty("name")
            .GetString().Should().Be("product");
        document.RootElement.GetProperty("sources")
            .GetArrayLength().Should().Be(5);
        var selection = fixture.Catalog.Resolve("product");
        selection.Definition.Sources.Should().BeEquivalentTo(new[]
        {
            new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj"),
            new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Core/Core.csproj"),
            new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Jobs/Jobs.csproj"),
            new WorkspaceSource(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json"),
            new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md")
        });
        fixture.FileSystem.File.Exists(selection.ConfigurationPath).Should().BeTrue();
        fixture.FileSystem.File.Exists(selection.Workspace.DatabasePath).Should().BeFalse();
        fixture.FileSystem.File.Exists("/repo/sharpsense.yaml").Should().BeFalse();
    }

    [Fact]
    public async Task WhenManagingWorkspaceSources_ThenExistingDatabasesRemainUnchanged()
    {
        var fixture = new Fixture();
        (await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "create",
            "backend",
            "--repo-root",
            "/repo",
            "--csharp",
            "src/Api/Api.csproj")).ExitCode.Should().Be(0);
        (await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "create",
            "frontend",
            "--repo-root",
            "/repo",
            "--typescript",
            "frontend/tsconfig.json")).ExitCode.Should().Be(0);
        var backend = fixture.Catalog.Resolve("backend");
        fixture.FileSystem.AddFile(backend.Workspace.DatabasePath, new MockFileData("preserved database"));

        (await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "add",
            "backend",
            "--repo-root",
            "/repo",
            "--markdown",
            "docs/**/*.md")).ExitCode.Should().Be(0);
        fixture.Catalog.Resolve("backend").Definition.Sources.Should().BeEquivalentTo(new[]
        {
            new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj"),
            new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md")
        });

        (await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "remove",
            "backend",
            "--repo-root",
            "/repo",
            "--markdown",
            "docs/**/*.md")).ExitCode.Should().Be(0);
        var merged = await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "merge",
            "product",
            "backend",
            "frontend",
            "--json");

        merged.ExitCode.Should().Be(0, merged.Output);
        var product = fixture.Catalog.Resolve("product");
        product.Definition.Sources.Should().BeEquivalentTo(new[]
        {
            new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj"),
            new WorkspaceSource(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json")
        });
        product.Definition.Id.Should().NotBe(backend.Definition.Id);
        fixture.Catalog.List().Should().HaveCount(3);
        fixture.Catalog.Resolve("backend").Definition.Sources.Should()
            .ContainSingle().Which.Should().Be(new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj"));
        fixture.FileSystem.File.ReadAllText(backend.Workspace.DatabasePath).Should().Be("preserved database");
    }

    [Fact]
    public async Task WhenWorkspaceCreate_ThenRespectsAnExplicitRootInsideAGitRepository()
    {
        var fixture = new Fixture();

        var result = await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "create",
            "api",
            "--repo-root",
            "/repo/src/Api",
            "--csharp",
            "Api.csproj");

        result.ExitCode.Should().Be(0, result.Output);
        var workspace = fixture.Catalog.Resolve("api");
        workspace.Definition.WorkspaceRoot.Should().Be("/repo/src/Api");
        workspace.Definition.Sources.Should().ContainSingle(source => source.Path == "Api.csproj");
    }

    [Fact]
    public async Task WhenAnalyze_ThenRejectsLegacyPositionalTargetAndExplainsMissingWorkspace()
    {
        var fixture = new Fixture();

        var legacy = await fixture.Run(TestContext.Current.CancellationToken, "analyze", "src/Api/Api.csproj", "--repo-root", "/repo");
        var unregistered = await fixture.Run(TestContext.Current.CancellationToken, "analyze", "--repo-root", "/repo", "--no-embeddings");

        legacy.ExitCode.Should().NotBe(0);
        unregistered.ExitCode.Should().Be(1);
        unregistered.Output.Should().Contain("No workspace is selected");
        fixture.Catalog.List().Should().BeEmpty();
    }

    [Fact]
    public async Task WhenMalformedSibling_ThenDoesNotPolluteWorkspaceJsonOutput()
    {
        var fixture = new Fixture();
        var healthy = fixture.Catalog.Create("healthy", "/repo", []);
        var broken = fixture.Catalog.Create("broken", "/repo", []);
        fixture.FileSystem.File.WriteAllText(broken.ConfigurationPath, "private: [ malformed yaml");

        var listed = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "list", "--json");
        var selected = await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "show",
            "--workspace",
            healthy.Definition.Id.ToString(),
            "--json");

        listed.ExitCode.Should().Be(0);
        selected.ExitCode.Should().Be(0);
        using var list = JsonDocument.Parse(listed.Output);
        using var detail = JsonDocument.Parse(selected.Output);
        list.RootElement.EnumerateArray().Should().ContainSingle().Which.GetProperty("name")
            .GetString().Should().Be("healthy");
        detail.RootElement.GetProperty("name")
            .GetString().Should().Be("healthy");
        listed.Output.Should().NotContain("Skipping");
        listed.Output.Should().NotContain("private");
    }

    [Fact]
    public async Task WhenWorkspaceList_ThenIsReadOnlyAndExplicitSelectionResolvesAmbiguity()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create(
            "backend",
            "/repo",
            [new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj")]);
        fixture.Catalog.Create(
            "frontend",
            "/repo",
            [new WorkspaceSource(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json")]);
        var filesBefore = fixture.FileSystem.AllFiles
            .ToDictionary(path => path, path => fixture.FileSystem.File.ReadAllText(path));

        var listed = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "list", "--json");
        var ambiguous = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "show", "--repo-root", "/repo");
        var selected = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "show", "--workspace", "frontend", "--json");

        listed.ExitCode.Should().Be(0);
        ambiguous.ExitCode.Should().Be(1);
        ambiguous.Output.Should().Contain("No workspace is selected");
        selected.ExitCode.Should().Be(0, selected.Output);
        using var document = JsonDocument.Parse(selected.Output);
        document.RootElement.GetProperty("name")
            .GetString().Should().Be("frontend");
        fixture.FileSystem.AllFiles
            .ToDictionary(path => path, path => fixture.FileSystem.File.ReadAllText(path))
            .Should()
            .BeEquivalentTo(filesBefore);
    }

    [Fact]
    public async Task WhenUse_ThenPersistsDefaultAndLsMarksItWhileShowCanOverrideIt()
    {
        var fixture = new Fixture();
        var first = fixture.Catalog.Create("first", "/repo", []);
        var second = fixture.Catalog.Create("second", "/repo", []);

        var used = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "use", "second", "--json");
        var shown = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "show", "--repo-root", "/elsewhere", "--json");
        var overridden = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "show", "--workspace", "first", "--json");
        var listed = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "ls", "--json");

        used.ExitCode.Should().Be(0);
        shown.ExitCode.Should().Be(0);
        overridden.ExitCode.Should().Be(0);
        listed.ExitCode.Should().Be(0);
        using var defaultJson = JsonDocument.Parse(shown.Output);
        using var overrideJson = JsonDocument.Parse(overridden.Output);
        using var listJson = JsonDocument.Parse(listed.Output);
        defaultJson.RootElement.GetProperty("id")
            .GetGuid().Should().Be(second.Definition.Id);
        overrideJson.RootElement.GetProperty("id")
            .GetGuid().Should().Be(first.Definition.Id);
        listJson.RootElement.EnumerateArray().Should().ContainSingle(item => item.GetProperty("isDefault")
            .GetBoolean()).Which.GetProperty("id")
            .GetGuid().Should().Be(second.Definition.Id);
        (await fixture.Run(TestContext.Current.CancellationToken, "workspace", "ls")).Output.Should().Contain("(default)");

        var noExplicitMcp = await fixture.Run(TestContext.Current.CancellationToken, "mcp", "--workspace-root", "/repo");
        noExplicitMcp.ExitCode.Should().NotBe(0);
        noExplicitMcp.Output.Should().Contain("Multiple workspaces match");
        fixture.Catalog.GetDefaultWorkspaceId().Should().Be(second.Definition.Id);
        fixture.FileSystem.File.Exists(second.Workspace.DatabasePath).Should().BeFalse();
    }

    [Fact]
    public void WhenExplicitMcpWorkspaceBindings_ThenRemainIndependentOfDefaultChanges()
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

        firstSelection.Definition.Id.Should().Be(first.Definition.Id);
        secondSelection.Definition.Id.Should().Be(second.Definition.Id);
        firstHost.GetRequiredService<WorkspaceSelection>().Should().BeSameAs(firstSelection);
        secondHost.GetRequiredService<WorkspaceSelection>().Should().BeSameAs(secondSelection);

        ServiceProvider Bind(string name)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IFileSystem>(fixture.FileSystem);
            services.AddSingleton<IWorkspaceCatalog>(fixture.Catalog);
            services.AddSelectedWorkspace(
                new McpCommand.Settings
                {
                    Workspace = name
                },
                discoverFromDirectory: true);

            return services.BuildServiceProvider();
        }
    }

    [Theory]
    [InlineData("elsewhere")]
    [InlineData("invalid")]
    [InlineData("missing")]
    public void WhenMcpDiscoversAWorkspace_ThenItIgnoresTheSavedDefaultAndKeepsItsInitialBinding(string defaultState)
    {
        var fixture = new Fixture();
        var expected = fixture.Catalog.Create("product", "/repo", []);
        fixture.FileSystem.AddDirectory("/other");
        var other = fixture.Catalog.Create("elsewhere", "/other", []);
        fixture.Catalog.Use("elsewhere");
        if (defaultState == "invalid")
        {
            fixture.FileSystem.File.WriteAllText("/test-home/.sharpsense/default-workspace", "invalid");
        }
        else if (defaultState == "missing")
        {
            fixture.FileSystem.File.Delete(other.ConfigurationPath);
        }

        var services = new ServiceCollection();
        services.AddSingleton<IFileSystem>(fixture.FileSystem);
        services.AddSingleton<IWorkspaceCatalog>(fixture.Catalog);
        services.AddSelectedWorkspace(
            new McpCommand.Settings
            {
                WorkspaceRoot = "/repo/frontend"
            },
            discoverFromDirectory: true);
        using var host = services.BuildServiceProvider();

        var selection = host.GetRequiredService<WorkspaceSelection>();
        fixture.Catalog.Use("product");
        fixture.Catalog.Create("overlapping", "/repo", []);

        selection.Definition.Id.Should().Be(expected.Definition.Id);
        host.GetRequiredService<WorkspaceSelection>().Should().BeSameAs(selection);
        host.GetRequiredService<IRepositoryWorkspace>().WorkspaceId.Should().Be(expected.Definition.Id);
    }

    [Theory]
    [InlineData("--workspace-root")]
    [InlineData("--repo-root")]
    public async Task WhenRootOptionIsUsed_ThenCreationPreservesTheRootAndJsonContract(string option)
    {
        var fixture = new Fixture();

        var result = await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "create",
            "frontend",
            option,
            "/repo/frontend",
            "--typescript",
            "tsconfig.json",
            "--json");

        result.ExitCode.Should().Be(0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("repositoryRoot")
            .GetString().Should().Be("/repo/frontend");
        var selection = fixture.Catalog.Resolve("frontend");
        selection.Definition.WorkspaceRoot.Should().Be("/repo/frontend");
        selection.Definition.Sources.Should().ContainSingle().Which.Path.Should().Be("tsconfig.json");
    }

    [Fact]
    public async Task WhenCliHasOneDirectoryMatchAndNoDefault_ThenItStillRequiresWorkspaceSelection()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("product", "/repo", []);

        var result = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "show", "--workspace-root", "/repo");

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("No workspace is selected");
        result.Output.Should().Contain("workspace use");
    }

    [Fact]
    public async Task WhenLs_ThenUsesTheSameJsonOutputAsList()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("docs", "/repo", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);

        var listed = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "list", "--json");
        var alias = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "ls", "--json");

        alias.ExitCode.Should().Be(0);
        alias.Output.Should().Be(listed.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenSourceCommands_ThenUseActualWorkingDirectoryDespiteStalePwd(bool useExplicitBase)
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
            fixture.FileSystem.AddFile(
                Path.Combine(repositoryRoot, ".git", "HEAD"),
                new MockFileData("ref: refs/heads/main"));
            fixture.FileSystem.AddFile(Path.Combine(sourceDirectory, "Api.csproj"), new MockFileData("<Project />"));
            fixture.FileSystem.AddFile(
                Path.Combine(repositoryRoot, "src", "Core", "Core.csproj"),
                new MockFileData("<Project />"));

            Environment.CurrentDirectory = useExplicitBase ? taskRoot : repositoryRoot;
            Environment.SetEnvironmentVariable("PWD", sourceDirectory);
            string[] createRootOptions = useExplicitBase ? ["--workspace-root", "repo"] : [];

            var created = await fixture.Run(
                TestContext.Current.CancellationToken,
                [
                    "workspace",
                    "create",
                    "api",
                    .. createRootOptions,
                    "--csharp",
                    "src/Api/Api.csproj",
                    "--markdown",
                    "src/Api/docs/*.md",
                    "--json"
                ]);
            created.ExitCode.Should().Be(0, created.Output);
            fixture.Catalog.AddSources("api", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/*.md")]);

            Environment.CurrentDirectory = useExplicitBase ? taskRoot : sourceDirectory;
            Environment.SetEnvironmentVariable("PWD", repositoryRoot);
            string[] rootOptions = useExplicitBase ? ["--workspace-root", "repo/src/Api"] : [];

            var added = await fixture.Run(
                TestContext.Current.CancellationToken,
                ["workspace", "add", "api", .. rootOptions, "--csharp", "../Core/Core.csproj", "--json"]);

            added.ExitCode.Should().Be(0);
            var sources = fixture.Catalog.Resolve("api").Definition.Sources;
            sources.Length.Should().Be(4);
            sources.Should().Contain(new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj"));
            sources.Should().Contain(new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Core/Core.csproj"));
            sources.Should().Contain(new WorkspaceSource(WorkspaceSourceKind.Markdown, "src/Api/docs/*.md"));

            var removed = await fixture.Run(
                TestContext.Current.CancellationToken,
                [
                    "workspace",
                    "remove",
                    "api",
                    .. rootOptions,
                    "--csharp",
                    "../Core/Core.csproj",
                    "--markdown",
                    "docs/*.md",
                    "--markdown",
                    "missing/**/*.md",
                    "--json"
                ]);

            removed.ExitCode.Should().Be(0);
            using var document = JsonDocument.Parse(removed.Output);
            document.RootElement.GetProperty("name")
                .GetString().Should().Be("api");
            document.RootElement.GetProperty("removedSources")
                .GetArrayLength().Should().Be(2);
            document.RootElement.GetProperty("unmatchedSources")
                .EnumerateArray().Should().ContainSingle().Which.GetProperty("path")
                .GetString().Should().Be("src/Api/missing/**/*.md");
            var remaining = fixture.Catalog.Resolve("api").Definition.Sources;
            remaining.Length.Should().Be(2);
            remaining.Should().Contain(new WorkspaceSource(WorkspaceSourceKind.CSharp, "src/Api/Api.csproj"));
            remaining.Should().Contain(new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/*.md"));
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
    public async Task WhenRemove_ThenReportsNoMatchingSourcesWithoutClaimingAnUpdate(bool json)
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("docs", "/repo", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        string[] outputOptions = json ? ["--json"] : [];

        var result = await fixture.Run(
            TestContext.Current.CancellationToken,
            [
                "workspace",
                "remove",
                "docs",
                "--repo-root",
                "/repo",
                "--markdown",
                "typo/**/*.md",
                .. outputOptions
            ]);

        result.ExitCode.Should().Be(0);
        fixture.Catalog.Resolve("docs").Definition.Sources.Should()
            .ContainSingle().Which.Should().Be(new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md"));
        if (json)
        {
            using var document = JsonDocument.Parse(result.Output);
            document.RootElement.GetProperty("removedSources")
                .EnumerateArray().Should().BeEmpty();
            document.RootElement.GetProperty("unmatchedSources")
                .EnumerateArray().Should().ContainSingle().Which.GetProperty("path")
                .GetString().Should().Be("typo/**/*.md");
        }
        else
        {
            result.Output.Should().Contain("Removed 0 source(s)");
            result.Output.Should().Contain("Not registered Markdown: typo/**/*.md");
            result.Output.Should().NotContain("Updated workspace");
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
    public async Task WhenDisplayingCatalogHelp_ThenOnlyRelevantOptionsAreAdvertised(string command, bool hasSourceBase)
    {
        var result = await new Fixture().Run(TestContext.Current.CancellationToken, "workspace", command, "--help");

        result.ExitCode.Should().Be(0);
        var options = result.Output[result.Output.IndexOf("OPTIONS:", StringComparison.Ordinal)..];
        options.Should().NotContain("--workspace ");
        options.Contains("--workspace-root", StringComparison.Ordinal).Should().Be(hasSourceBase);
    }

    [Fact]
    public async Task WhenIgnoredWorkspaceFlags_ThenAreRejectedAndShowRejectsConflictingSelectors()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("docs", "/repo", [new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);

        var unsupported = await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "add",
            "docs",
            "--workspace",
            "other",
            "--repo-root",
            "/repo",
            "--markdown",
            "more/**/*.md");
        var conflicting = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "show", "docs", "--workspace", "other");

        unsupported.ExitCode.Should().NotBe(0);
        unsupported.Output.Should().Contain("Unknown option");
        conflicting.ExitCode.Should().NotBe(0);
        conflicting.Output.Should().Contain("either a workspace argument or --workspace");
        fixture.Catalog.Resolve("docs").Definition.Sources.Should()
            .ContainSingle().Which.Should().Be(new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md"));
    }

    [Theory]
    [InlineData("list", false)]
    [InlineData("ls", false)]
    [InlineData("list", true)]
    [InlineData("ls", true)]
    public async Task WhenListingWithMalformedDefault_ThenPreservesOutputAndWarnsOnStderr(string command, bool json)
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("first", "/repo", []);
        fixture.Catalog.Create("second", "/repo", []);
        fixture.Catalog.Use("first");
        var defaultPath = Path.Combine(fixture.Catalog.HomeDirectory, "default-workspace");
        fixture.FileSystem.File.WriteAllText(defaultPath, "invalid");
        var filesBefore = fixture.FileSystem.AllFiles.Order()
            .ToArray();
        var previousError = Console.Error;
        using var errors = new StringWriter();
        string[] options = json ? ["--json"] : [];

        try
        {
            Console.SetError(errors);
            var result = await fixture.Run(TestContext.Current.CancellationToken, ["workspace", command, .. options]);

            result.ExitCode.Should().Be(0);
            errors.ToString().Should().Contain("Warning:");
            errors.ToString().Should().Contain("workspace use");
            result.Output.Should().NotContain("Warning:");
            if (json)
            {
                using var document = JsonDocument.Parse(result.Output);
                document.RootElement.EnumerateArray()
                    .Select(item => item.GetProperty("name").GetString())
                    .Should()
                    .BeEquivalentTo(new[] { "first", "second" });
                document.RootElement.EnumerateArray().Should().AllSatisfy(item => item.GetProperty("isDefault")
                    .GetBoolean().Should().BeFalse());
            }
            else
            {
                result.Output.Should().Contain("first");
                result.Output.Should().Contain("second");
                result.Output.Should().NotContain("(default)");
            }

            fixture.FileSystem.File.ReadAllText(defaultPath).Should().Be("invalid");
            fixture.FileSystem.AllFiles.Order().Should().Equal(filesBefore);
        }
        finally
        {
            Console.SetError(previousError);
        }

        (await fixture.Run(TestContext.Current.CancellationToken, "workspace", "use", "second")).ExitCode.Should().Be(0);
        fixture.Catalog.Resolve(null).Definition.Name.Should().Be("second");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task WhenBlank_ThenShowArgumentCannotFallBackToTheDefaultOrMaskAnExplicitWorkspace(string selector)
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("first", "/repo", []);
        fixture.Catalog.Create("second", "/repo", []);
        fixture.Catalog.Use("first");

        var blank = await fixture.Run(TestContext.Current.CancellationToken, "workspace", "show", selector, "--json");
        var conflict = await fixture.Run(
            TestContext.Current.CancellationToken,
            "workspace",
            "show",
            selector,
            "--workspace",
            "second",
            "--json");

        blank.ExitCode.Should().NotBe(0);
        blank.Output.Should().Contain("must not be empty");
        conflict.ExitCode.Should().NotBe(0);
        conflict.Output.Should().Contain("either a workspace argument or --workspace");
        fixture.Catalog.Resolve(null).Definition.Name.Should().Be("first");
    }

    private sealed class Fixture
    {
        public MockFileSystem FileSystem { get; } = new(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/src/Api/Api.csproj"] = new("<Project />"),
                ["/repo/src/Core/Core.csproj"] = new("<Project />"),
                ["/repo/src/Jobs/Jobs.csproj"] = new("<Project />"),
                ["/repo/frontend/tsconfig.json"] = new("{}"),
                ["/repo/docs/guide.md"] = new("# Guide")
            },
            "/repo");

        public WorkspaceCatalog Catalog { get; }

        public Fixture() => Catalog = new WorkspaceCatalog(FileSystem, "/test-home/.sharpsense");

        public async Task<(int ExitCode, string Output)> Run(CancellationToken ct, params string[] args)
        {
            using var console = new TestConsole();
            var app = Cli.Program.CreateCommandApp(
                console,
                services =>
                {
                    services.AddSingleton<IFileSystem>(FileSystem);
                    services.AddSingleton<IWorkspaceCatalog>(Catalog);
                },
                enableFileLogging: false);
            var exitCode = await app.RunAsync(args, ct);

            return (exitCode, console.Output);
        }
    }
}
