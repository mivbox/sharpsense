using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Analyze;
using SharpSense.Cli.Workspaces;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Testing;

namespace SharpSense.IntegrationTests;

public sealed class WorkspaceInteractionsTests
{
    [Fact]
    public async Task BareWorkspaceUsesInjectedManagerWhileRootShowsHelp()
    {
        var fixture = new Fixture();
        fixture.Interactions.Setup(x => x.SelectAction(null, It.IsAny<CancellationToken>())).ReturnsAsync(WorkspaceAction.Exit);
        var manager = await fixture.Run("workspace");
        Assert.Equal(0, manager.Exit);
        fixture.Interactions.Verify(x => x.SelectAction(null, It.IsAny<CancellationToken>()), Times.Once);
        var root = await fixture.Run();
        Assert.Equal(0, root.Exit);
        Assert.Contains("USAGE", root.Output);
        fixture.Interactions.Verify(x => x.SelectAction(null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BareWorkspaceInRedirectedModeExplainsAutomationCommands()
    {
        var fixture = new Fixture();
        fixture.Interactions.SetupGet(x => x.IsInteractive).Returns(false);
        var result = await fixture.Run("workspace");
        Assert.Equal(1, result.Exit);
        Assert.Contains("interactive terminal", result.Output);
        Assert.Contains("rename", result.Output);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigureReviewsBeforeSavingAndCanCancel(bool save)
    {
        var fixture = new Fixture();
        fixture.SetupCreation(save);
        var result = await fixture.Run("configure", "--repo-root", "/repo");
        Assert.Equal(0, result.Exit);
        Assert.Equal(save ? 1 : 0, fixture.Catalog.List().Count);
        fixture.Interactions.Verify(x => x.ShowConfiguration("guided", "/repo/docs", It.IsAny<IReadOnlyList<WorkspaceSource>>()), Times.Once);
        if (save) Assert.Equal("docs/*.md", Assert.Single(fixture.Catalog.List()[0].Definition.Sources).Path);
    }

    [Fact]
    public async Task JsonConfigureNeverPromptsForMissingArguments()
    {
        var fixture = new Fixture();
        var result = await fixture.Run("configure", "--repo-root", "/repo", "--json");
        Assert.Equal(1, result.Exit);
        Assert.Contains("JSON mode requires", result.Output);
        fixture.Interactions.Verify(x => x.ReadName(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AmbiguousAnalysisPromptsBeforeBindingAndExplicitUnknownNeverPrompts()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("one", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        var second = fixture.Catalog.Create("two", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        fixture.Interactions.Setup(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>())).ReturnsAsync(second);
        var management = new WorkspaceManagement(fixture.Catalog, fixture.Interactions.Object);
        var selected = await management.SelectForAnalysis(null, "/repo", TestContext.Current.CancellationToken);
        Assert.Equal(second.Definition.Id, selected!.Definition.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => management.SelectForAnalysis("missing", "/repo", TestContext.Current.CancellationToken));
        fixture.Interactions.Verify(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnregisteredInteractiveAnalysisCanCreateWorkspace()
    {
        var fixture = new Fixture();
        fixture.SetupCreation(true);
        fixture.Interactions.Setup(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>())).ReturnsAsync((WorkspaceSelection?)null);
        var selected = await new WorkspaceManagement(fixture.Catalog, fixture.Interactions.Object)
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
    public async Task ManagerUpdatesSourcesAndRunsSelectedWorkspaceWithoutNestedCli()
    {
        var fixture = new Fixture();
        var original = fixture.Catalog.Create("selected", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        var added = new WorkspaceSource(WorkspaceSourceKind.CSharp, "/repo/Api.csproj");
        fixture.Interactions.SetupSequence(x => x.SelectAction(It.IsAny<WorkspaceSelection?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceAction.AddSources).ReturnsAsync(WorkspaceAction.RemoveSources)
            .ReturnsAsync(WorkspaceAction.Watch).ReturnsAsync(WorkspaceAction.Exit);
        fixture.Interactions.Setup(x => x.SelectSources("/repo", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { added });
        fixture.Interactions.Setup(x => x.SelectSourcesToRemove(It.IsAny<IReadOnlyList<WorkspaceSource>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/*.md") });
        fixture.Interactions.Setup(x => x.ShowConfiguration(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkspaceSource>>()));
        fixture.Interactions.Setup(x => x.Confirm(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var calls = 0;
        await new WorkspaceManagement(fixture.Catalog, fixture.Interactions.Object).Manage("selected", "/repo", (selection, watch, _) =>
        {
            Assert.True(watch);
            Assert.Equal(original.Definition.Id, selection.Definition.Id);
            Assert.Equal("Api.csproj", Assert.Single(selection.Definition.Sources).Path);
            calls++;
            return Task.FromResult(0);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ManagerRenamePreservesSourcesAddedSinceSelection()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("original", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        fixture.Interactions.SetupSequence(x => x.SelectAction(It.IsAny<WorkspaceSelection?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkspaceAction.Rename).ReturnsAsync(WorkspaceAction.Exit);
        fixture.Interactions.Setup(x => x.ReadName("original", It.IsAny<CancellationToken>())).ReturnsAsync("renamed");
        fixture.Interactions.Setup(x => x.Confirm(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(() =>
        {
            fixture.Catalog.AddSources("original", [new(WorkspaceSourceKind.CSharp, "Api.csproj")]);
            return true;
        });
        await new WorkspaceManagement(fixture.Catalog, fixture.Interactions.Object).Manage("original", "/repo",
            (_, _, _) => throw new InvalidOperationException("Analysis not expected."), TestContext.Current.CancellationToken);
        Assert.Equal(2, fixture.Catalog.Resolve("renamed", "/repo").Definition.Sources.Length);
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
        public Mock<IAnalyzeInteractions> Interactions { get; } = new(MockBehavior.Strict);

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
