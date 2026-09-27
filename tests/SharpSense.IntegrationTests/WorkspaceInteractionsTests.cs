using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Workspaces;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceInteractionsTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 0)]
    public async Task BareWorkspaceAndRootShowHelpWithoutStartingServices(bool workspace, bool explicitHelp, int expectedExit)
    {
        using var console = new TestConsole();
        var app = Cli.Program.CreateCommandApp(console,
            _ => throw new InvalidOperationException("Help must not start command services."),
            enableFileLogging: false);

        string[] args = workspace ? ["workspace"] : [];
        if (explicitHelp)
        {
            args = [.. args, "--help"];
        }
        var exit = await app.RunAsync(args, TestContext.Current.CancellationToken);

        // Spectre reports a missing subcommand with help and exit code 1.
        Assert.Equal(expectedExit, exit);
        Assert.Contains("USAGE", console.Output);
        Assert.Contains(workspace ? "create" : "workspace", console.Output);
    }

    [Fact]
    public async Task AnalysisUsesDefaultWhileExplicitSelectionOverridesIt()
    {
        var fixture = new Fixture();
        var first = fixture.Catalog.Create("first", "/repo", []);
        var second = fixture.Catalog.Create("second", "/repo", []);
        fixture.Catalog.Use("second");

        var setup = new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object);
        Assert.Equal(second.Definition.Id, (await setup.SelectForAnalysis(null, "/elsewhere", TestContext.Current.CancellationToken))!.Definition.Id);
        Assert.Equal(first.Definition.Id, (await setup.SelectForAnalysis("first", "/elsewhere", TestContext.Current.CancellationToken))!.Definition.Id);
        fixture.Interactions.Verify(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task BlankAnalysisSelectorNeverOpensThePicker(string selector)
    {
        var fixture = new Fixture();
        var setup = new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object);

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            setup.SelectForAnalysis(selector, "/repo", TestContext.Current.CancellationToken));

        fixture.Interactions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnavailableDefaultDoesNotOpenTheAnalysisPicker()
    {
        var fixture = new Fixture();
        var selected = fixture.Catalog.Create("selected", "/repo", []);
        fixture.Catalog.Use("selected");
        fixture.FileSystem.File.Delete(selected.ConfigurationPath);

        var setup = new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            setup.SelectForAnalysis(null, "/repo", TestContext.Current.CancellationToken));

        Assert.Contains("workspace use", error.Message);
        fixture.Interactions.Verify(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task CreationReviewsBeforeSavingAndCanCancel(bool alias, bool save)
    {
        var fixture = new Fixture();
        fixture.SetupCreation(save);
        string[] command = alias ? ["configure"] : ["workspace", "create"];
        var result = await fixture.Run([.. command, "--repo-root", "/repo"]);
        Assert.Equal(0, result.Exit);
        Assert.Equal(save ? 1 : 0, fixture.Catalog.List().Count);
        fixture.Interactions.Verify(x => x.ShowConfiguration("guided", "/repo/docs", It.IsAny<IReadOnlyList<WorkspaceSource>>()), Times.Once);
        if (save) Assert.Equal("docs/*.md", Assert.Single(fixture.Catalog.List()[0].Definition.Sources).Path);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JsonCreationNeverPromptsForMissingArguments(bool alias)
    {
        var fixture = new Fixture();
        string[] command = alias ? ["configure"] : ["workspace", "create"];
        var result = await fixture.Run([.. command, "--repo-root", "/repo", "--json"]);
        Assert.Equal(1, result.Exit);
        Assert.Contains("JSON mode requires", result.Output);
        fixture.Interactions.Verify(x => x.ReadName(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task FullySpecifiedCreationNeverPrompts(bool alias, bool json)
    {
        var fixture = new Fixture();
        string[] command = alias ? ["configure"] : ["workspace", "create"];
        string[] outputOptions = json ? ["--json"] : [];

        var result = await fixture.Run([.. command, "explicit", "--repo-root", "/repo",
            "--csharp", "Api.csproj", .. outputOptions]);

        Assert.Equal(0, result.Exit);
        Assert.Single(fixture.Catalog.List());
        fixture.Interactions.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RedirectedCreationRejectsIncompleteInputsWithoutWritingAWorkspace(bool alias)
    {
        var fixture = new Fixture();
        fixture.Interactions.SetupGet(x => x.IsInteractive).Returns(false);
        string[] command = alias ? ["configure"] : ["workspace", "create"];

        var result = await fixture.Run([.. command, "incomplete", "--repo-root", "/repo"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("Provide a workspace name", result.Output);
        Assert.Empty(fixture.Catalog.List());
        fixture.Interactions.Verify(x => x.ReadName(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AmbiguousAnalysisPromptsBeforeBindingAndExplicitUnknownNeverPrompts()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("one", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        var second = fixture.Catalog.Create("two", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        fixture.Interactions.Setup(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>())).ReturnsAsync(second);
        var setup = new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object);
        var selected = await setup.SelectForAnalysis(null, "/repo", TestContext.Current.CancellationToken);
        Assert.Equal(second.Definition.Id, selected!.Definition.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.SelectForAnalysis("missing", "/repo", TestContext.Current.CancellationToken));
        fixture.Interactions.Verify(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisteredInteractiveAnalysisCanCreateWorkspace()
    {
        var fixture = new Fixture();
        fixture.SetupCreation(true);
        fixture.Interactions.Setup(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceSelection?)null);
        var selected = await new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object)
            .SelectForAnalysis(null, "/repo", TestContext.Current.CancellationToken);
        Assert.Equal("guided", selected!.Definition.Name);
        Assert.Single(fixture.Catalog.List());
    }

    [Fact]
    public async Task RenamePreservesWorkspaceIdentityAndRejectsDuplicateNamesOrHeldLease()
    {
        var fixture = new Fixture();
        var original = fixture.Catalog.Create("old", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        fixture.FileSystem.AddFile(original.Workspace.DatabasePath, new MockFileData("preserved graph"));
        var result = await fixture.Run("workspace", "rename", "old", "new", "--json");
        Assert.Equal(0, result.Exit);
        var renamed = fixture.Catalog.Resolve("new", "/repo");
        Assert.Equal(original.Definition.Id, renamed.Definition.Id);
        Assert.Equal("preserved graph", fixture.FileSystem.File.ReadAllText(renamed.Workspace.DatabasePath));
        fixture.Catalog.Create("taken", "/repo", []);
        Assert.Equal(1, (await fixture.Run("workspace", "rename", "new", "taken")).Exit);
        using (fixture.Catalog.AcquireIndexLease(renamed))
        {
            var locked = await fixture.Run("workspace", "rename", "new", "blocked");
            Assert.Equal(1, locked.Exit);
            Assert.Contains("already being indexed or watched", locked.Output);
        }
        Assert.Equal("new", fixture.Catalog.ResolveById(original.Definition.Id).Definition.Name);
    }

    [Fact]
    public void SharedDiscoveryStaysBoundedToSourceCandidatesAndIgnoresBuildDirectories()
    {
        var fixture = new Fixture();
        fixture.FileSystem.AddFile("/repo/node_modules/pkg/tsconfig.json", new MockFileData("{}"));
        fixture.FileSystem.AddFile("/repo/obj/Hidden.csproj", new MockFileData(""));
        fixture.FileSystem.AddFile("/repo/frontend/tsconfig.json", new MockFileData("{}"));
        var discovered = new WorkspaceSourceDiscovery(fixture.FileSystem).Discover("/repo", TestContext.Current.CancellationToken);
        Assert.Equal("/repo", discovered.RepositoryRoot);
        Assert.Equal(3, discovered.Sources.Count);
        Assert.Contains(new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/*.md"), discovered.Sources);
        Assert.DoesNotContain(discovered.Sources, source => source.Path.StartsWith("node_modules") || source.Path.StartsWith("obj"));
    }

    private sealed class Fixture
    {
        public MockFileSystem FileSystem { get; } = new(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
            ["/repo/Api.csproj"] = new("<Project />"),
            ["/repo/docs/guide.md"] = new("# Guide")
        }, "/repo");
        public WorkspaceCatalog Catalog { get; }
        public Mock<IWorkspaceInteractions> Interactions { get; } = new(MockBehavior.Strict);

        public Fixture()
        {
            Catalog = new WorkspaceCatalog(FileSystem, "/home/.sharpsense");
            Interactions.SetupGet(x => x.IsInteractive).Returns(true);
        }

        public void SetupCreation(bool save)
        {
            Interactions.Setup(x => x.ReadName(null, It.IsAny<CancellationToken>())).ReturnsAsync("guided");
            Interactions.Setup(x => x.ReadRepositoryRoot("/repo", It.IsAny<CancellationToken>())).ReturnsAsync("/repo/docs");
            Interactions.Setup(x => x.SelectSources("/repo/docs", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { new WorkspaceSource(WorkspaceSourceKind.Markdown, "/repo/docs/*.md") });
            Interactions.Setup(x => x.ShowConfiguration("guided", "/repo/docs", It.IsAny<IReadOnlyList<WorkspaceSource>>()));
            Interactions.Setup(x => x.Confirm("Save this workspace?", It.IsAny<CancellationToken>())).ReturnsAsync(save);
        }

        public async Task<(int Exit, string Output)> Run(params string[] args)
        {
            using var console = new TestConsole();
            var app = Cli.Program.CreateCommandApp(console, services =>
            {
                services.AddSingleton<IFileSystem>(FileSystem);
                services.AddSingleton(Catalog);
                services.AddSingleton(Interactions.Object);
            }, enableFileLogging: false);
            var exit = await app.RunAsync(args, TestContext.Current.CancellationToken);
            return (exit, console.Output);
        }
    }
}
