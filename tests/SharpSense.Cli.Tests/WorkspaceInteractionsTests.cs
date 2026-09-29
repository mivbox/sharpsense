using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Cli.Workspaces;
using SharpSense.Infrastructure.Storage;
using Spectre.Console.Testing;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Cli.Tests;

public sealed class WorkspaceInteractionsTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 0)]
    public async Task WhenBareWorkspaceAndRoot_ThenShowHelpWithoutStartingServices(bool workspace, bool explicitHelp, int expectedExit)
    {
        using var console = new TestConsole();
        var app = global::SharpSense.Cli.Program.CreateCommandApp(
            console,
            _ => throw new InvalidOperationException("Help must not start command services."),
            enableFileLogging: false);

        string[] args = workspace ? ["workspace"] : [];
        if (explicitHelp)
        {
            args = [.. args, "--help"];
        }
        var exit = await app.RunAsync(args, TestContext.Current.CancellationToken);

        // Spectre reports a missing subcommand with help and exit code 1.
        exit.Should().Be(expectedExit);
        console.Output.Should().Contain("USAGE");
        console.Output.Should().Contain(workspace ? "create" : "workspace");
    }

    [Fact]
    public async Task WhenAnalysis_ThenUsesDefaultWhileExplicitSelectionOverridesIt()
    {
        var fixture = new Fixture();
        var first = fixture.Catalog.Create("first", "/repo", []);
        var second = fixture.Catalog.Create("second", "/repo", []);
        fixture.Catalog.Use("second");

        var setup = new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object);
        (await setup.SelectForAnalysis(null, "/elsewhere", TestContext.Current.CancellationToken))!.Definition.Id.Should().Be(second.Definition.Id);
        (await setup.SelectForAnalysis("first", "/elsewhere", TestContext.Current.CancellationToken))!.Definition.Id.Should().Be(first.Definition.Id);
        fixture.Interactions.Verify(
            x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task WhenBlankAnalysisSelector_ThenNeverOpensThePicker(string selector)
    {
        var fixture = new Fixture();
        var setup = new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object);

        await ((Func<Task>)(() =>
            setup.SelectForAnalysis(selector, "/repo", TestContext.Current.CancellationToken))).Should().ThrowAsync<ArgumentException>();

        fixture.Interactions.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WhenUnavailableDefault_ThenDoesNotOpenTheAnalysisPicker()
    {
        var fixture = new Fixture();
        var selected = fixture.Catalog.Create("selected", "/repo", []);
        fixture.Catalog.Use("selected");
        fixture.FileSystem.File.Delete(selected.ConfigurationPath);

        var setup = new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object);
        var error = (await ((Func<Task>)(() =>
            setup.SelectForAnalysis(null, "/repo", TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<InvalidOperationException>()).Which;

        error.Message.Should().Contain("workspace use");
        fixture.Interactions.Verify(
            x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task WhenCreationReviewsBeforeSavingAndCan_ThenCancel(bool alias, bool save)
    {
        var fixture = new Fixture();
        fixture.SetupCreation(save);
        string[] command = alias ? ["configure"] : ["workspace", "create"];
        var result = await fixture.Run([.. command, "--repo-root", "/repo"]);
        result.Exit.Should().Be(0);
        fixture.Catalog.List().Count.Should().Be(save ? 1 : 0);
        fixture.Interactions.Verify(x => x.ShowConfiguration("guided", "/repo/docs", It.IsAny<IReadOnlyList<WorkspaceSource>>()), Times.Once);
        if (save)
        {
            fixture.Catalog.List()[0].Definition.Sources.Should().ContainSingle().Which.Path.Should().Be("*.md");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenJsonCreation_ThenNeverPromptsForMissingArguments(bool alias)
    {
        var fixture = new Fixture();
        string[] command = alias ? ["configure"] : ["workspace", "create"];
        var result = await fixture.Run([.. command, "--repo-root", "/repo", "--json"]);
        result.Exit.Should().Be(1);
        result.Output.Should().Contain("JSON mode requires");
        fixture.Interactions.Verify(x => x.ReadName(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task WhenFullySpecifiedCreation_ThenNeverPrompts(bool alias, bool json)
    {
        var fixture = new Fixture();
        string[] command = alias ? ["configure"] : ["workspace", "create"];
        string[] outputOptions = json ? ["--json"] : [];

        var result = await fixture.Run([.. command, "explicit", "--repo-root", "/repo",
            "--csharp", "Api.csproj", .. outputOptions]);

        result.Exit.Should().Be(0);
        fixture.Catalog.List().Should().ContainSingle();
        fixture.Interactions.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenRedirectedCreation_ThenRejectsIncompleteInputsWithoutWritingAWorkspace(bool alias)
    {
        var fixture = new Fixture();
        fixture.Interactions.SetupGet(x => x.IsInteractive)
            .Returns(false);
        string[] command = alias ? ["configure"] : ["workspace", "create"];

        var result = await fixture.Run([.. command, "incomplete", "--repo-root", "/repo"]);

        result.Exit.Should().Be(1);
        result.Output.Should().Contain("Provide a workspace name");
        fixture.Catalog.List().Should().BeEmpty();
        fixture.Interactions.Verify(x => x.ReadName(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WhenAmbiguousAnalysisPromptsBeforeBindingAndExplicitUnknown_ThenNeverPrompts()
    {
        var fixture = new Fixture();
        fixture.Catalog.Create("one", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        var second = fixture.Catalog.Create("two", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        fixture.Interactions.Setup(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(second);
        var setup = new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object);
        var selected = await setup.SelectForAnalysis(null, "/repo", TestContext.Current.CancellationToken);
        selected!.Definition.Id.Should().Be(second.Definition.Id);
        await ((Func<Task>)(() => setup.SelectForAnalysis("missing", "/repo", TestContext.Current.CancellationToken))).Should().ThrowExactlyAsync<InvalidOperationException>();
        fixture.Interactions.Verify(
            x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task WhenInteractiveAnalysisHasNoWorkspace_ThenUserCanCreateOne()
    {
        var fixture = new Fixture();
        fixture.SetupCreation(true);
        fixture.Interactions.Setup(x => x.SelectWorkspace(It.IsAny<IReadOnlyList<WorkspaceSelection>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkspaceSelection?)null);
        var selected = await new WorkspaceSetup(fixture.Catalog, fixture.Interactions.Object)
            .SelectForAnalysis(null, "/repo", TestContext.Current.CancellationToken);
        selected!.Definition.Name.Should().Be("guided");
        fixture.Catalog.List().Should().ContainSingle();
    }

    [Fact]
    public async Task WhenRename_ThenPreservesWorkspaceIdentityAndRejectsDuplicateNamesOrHeldLease()
    {
        var fixture = new Fixture();
        var original = fixture.Catalog.Create("old", "/repo", [new(WorkspaceSourceKind.Markdown, "docs/*.md")]);
        fixture.FileSystem.AddFile(original.Workspace.DatabasePath, new MockFileData("preserved graph"));
        var result = await fixture.Run("workspace", "rename", "old", "new", "--json");
        result.Exit.Should().Be(0);
        var renamed = fixture.Catalog.Resolve("new");
        renamed.Definition.Id.Should().Be(original.Definition.Id);
        fixture.FileSystem.File.ReadAllText(renamed.Workspace.DatabasePath).Should().Be("preserved graph");
        fixture.Catalog.Create("taken", "/repo", []);
        (await fixture.Run("workspace", "rename", "new", "taken")).Exit.Should().Be(1);
        using (fixture.Catalog.AcquireIndexLease(renamed))
        {
            var locked = await fixture.Run("workspace", "rename", "new", "blocked");
            locked.Exit.Should().Be(1);
            locked.Output.Should().Contain("already being indexed or watched");
        }
        fixture.Catalog.ResolveById(original.Definition.Id).Definition.Name.Should().Be("new");
    }

    [Fact]
    public void WhenDiscoveringSources_ThenOnlySourceCandidatesAreIncluded()
    {
        var fixture = new Fixture();
        fixture.FileSystem.AddFile("/repo/node_modules/pkg/tsconfig.json", new MockFileData("{}"));
        fixture.FileSystem.AddFile("/repo/obj/Hidden.csproj", new MockFileData(""));
        fixture.FileSystem.AddFile("/repo/frontend/tsconfig.json", new MockFileData("{}"));
        var discovered = new WorkspaceSourceDiscovery(fixture.FileSystem).Discover("/repo", TestContext.Current.CancellationToken);
        discovered.WorkspaceRoot.Should().Be("/repo");
        discovered.Sources.Count.Should().Be(3);
        discovered.Sources.Should().Contain(new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/*.md"));
        discovered.Sources.Should().NotContain(source => source.Path.StartsWith("node_modules") || source.Path.StartsWith("obj"));
    }

    private sealed class Fixture
    {
        public MockFileSystem FileSystem
        {
            get;
        } = new(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/Api.csproj"] = new("<Project />"),
                ["/repo/docs/guide.md"] = new("# Guide")
            },
            "/repo");
        public WorkspaceCatalog Catalog
        {
            get;
        }
        public Mock<IWorkspaceInteractions> Interactions
        {
            get;
        } = new(MockBehavior.Strict);

        public Fixture()
        {
            Catalog = new WorkspaceCatalog(FileSystem, "/home/.sharpsense");
            Interactions.SetupGet(x => x.IsInteractive)
                .Returns(true);
        }

        public void SetupCreation(bool save)
        {
            Interactions.Setup(x => x.ReadName(null, It.IsAny<CancellationToken>()))
                .ReturnsAsync("guided");
            Interactions.Setup(x => x.ReadWorkspaceRoot("/repo", It.IsAny<CancellationToken>()))
                .ReturnsAsync("/repo/docs");
            Interactions.Setup(x => x.SelectSources("/repo/docs", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[]
                {
                    new WorkspaceSource(WorkspaceSourceKind.Markdown, "/repo/docs/*.md")
                });
            Interactions.Setup(x => x.ShowConfiguration("guided", "/repo/docs", It.IsAny<IReadOnlyList<WorkspaceSource>>()));
            Interactions.Setup(x => x.Confirm("Save this workspace?", It.IsAny<CancellationToken>()))
                .ReturnsAsync(save);
        }

        public async Task<(int Exit, string Output)> Run(params string[] args)
        {
            using var console = new TestConsole();
            var app = global::SharpSense.Cli.Program.CreateCommandApp(
                console,
                services =>
            {
                services.AddSingleton<IFileSystem>(FileSystem);
                services.AddSingleton<IWorkspaceCatalog>(Catalog);
                services.AddSingleton(Interactions.Object);
            },
                enableFileLogging: false);
            var exit = await app.RunAsync(args, TestContext.Current.CancellationToken);

            return (exit, console.Output);
        }
    }
}
