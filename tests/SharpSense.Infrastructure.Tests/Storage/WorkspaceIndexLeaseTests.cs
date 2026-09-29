using AwesomeAssertions;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;

namespace SharpSense.Infrastructure.Tests.Storage;

public sealed class WorkspaceIndexLeaseTests
{
    [Fact]
    public void WhenIndependentCatalogs_ThenCannotIndexOrChangeSourcesWhileLeaseIsHeld()
    {
        using var fixture = new Fixture();
        var first = fixture.Catalog;
        var second = new WorkspaceCatalog(new FileSystem(), fixture.Home);
        var selection = first.Create("product", fixture.RepositoryRoot, [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var original = File.ReadAllText(selection.ConfigurationPath);
        using var lease = first.AcquireIndexLease(selection);

        var busy = ((Action)(() => second.AcquireIndexLease(second.Resolve("product")))).Should().ThrowExactly<WorkspaceIndexBusyException>().Which;
        busy.Message.Should().Contain("Stop that session");
        ((Action)(() => second.AddSources("product", [new(WorkspaceSourceKind.Markdown, "notes/**/*.md")]))).Should().ThrowExactly<WorkspaceIndexBusyException>();
        ((Action)(() => second.RemoveSources("product", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]))).Should().ThrowExactly<WorkspaceIndexBusyException>();
        ((Action)(() => second.Update("product", "renamed", []))).Should().ThrowExactly<WorkspaceIndexBusyException>();

        File.ReadAllText(selection.ConfigurationPath).Should().Be(original);
    }

    [Fact]
    public void WhenLeaseIsReleased_ThenItCanBeReacquiredWithoutDeletingLockFile()
    {
        using var fixture = new Fixture();
        var selection = fixture.Catalog.Create("product", fixture.RepositoryRoot, []);
        var lease = fixture.Catalog.AcquireIndexLease(selection);

        lease.Dispose();
        lease.Dispose();

        File.Exists(Path.Combine(selection.DirectoryPath, ".index.lock")).Should().BeTrue();
        var other = new WorkspaceCatalog(new FileSystem(), fixture.Home);
        using (other.AcquireIndexLease(other.Resolve("product")))
        {
            ((Action)(() => fixture.Catalog.AcquireIndexLease(selection))).Should().ThrowExactly<WorkspaceIndexBusyException>();
        }

        var updated = fixture.Catalog.AddSources("product", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        updated.Definition.Sources.Should().ContainSingle();
    }

    [Fact]
    public void WhenStaleSelection_ThenCannotAcquireWriterLeaseAfterSourcesChange()
    {
        using var fixture = new Fixture();
        var stale = fixture.Catalog.Create("product", fixture.RepositoryRoot, [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var current = fixture.Catalog.AddSources("product", [new(WorkspaceSourceKind.Markdown, "notes/**/*.md")]);

        var exception = ((Action)(() => fixture.Catalog.AcquireIndexLease(stale))).Should().ThrowExactly<WorkspaceDefinitionChangedException>().Which;

        exception.Message.Should().Contain("Restart indexing or watching");
        using var lease = fixture.Catalog.AcquireIndexLease(current);
    }

    [Fact]
    public void WhenWorkspacesDiffer_ThenTheyCanBeIndexedConcurrentlyAndMergedIndependently()
    {
        using var fixture = new Fixture();
        var first = fixture.Catalog.Create("first", fixture.RepositoryRoot, [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var second = fixture.Catalog.Create("second", fixture.RepositoryRoot, [new(WorkspaceSourceKind.Markdown, "notes/**/*.md")]);
        using var firstLease = fixture.Catalog.AcquireIndexLease(first);
        using var secondLease = fixture.Catalog.AcquireIndexLease(second);

        var merged = fixture.Catalog.Merge("combined", ["first", "second"]);

        merged.Definition.Sources.Length.Should().Be(2);
        using var mergedLease = fixture.Catalog.AcquireIndexLease(merged);
    }

    [Fact]
    public void WhenSelectionFromAnotherHome_ThenCannotLockUnrelatedStorage()
    {
        using var fixture = new Fixture();
        var selection = fixture.Catalog.Create("product", fixture.RepositoryRoot, []);
        var differentHome = Path.Combine(fixture.Directory.FullName, "different-home");
        var otherCatalog = new WorkspaceCatalog(new FileSystem(), differentHome);

        ((Action)(() => otherCatalog.AcquireIndexLease(selection))).Should().ThrowExactly<WorkspaceDefinitionChangedException>();

        System.IO.Directory.Exists(differentHome).Should().BeFalse();
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Directory = System.IO.Directory.CreateTempSubdirectory("sharpsense-index-lease-");
            RepositoryRoot = Path.Combine(Directory.FullName, "repository");
            Home = Path.Combine(Directory.FullName, "home");
            System.IO.Directory.CreateDirectory(Path.Combine(RepositoryRoot, ".git"));
            Catalog = new WorkspaceCatalog(new FileSystem(), Home);
        }

        public DirectoryInfo Directory
        {
            get;
        }

        public string RepositoryRoot
        {
            get;
        }

        public string Home
        {
            get;
        }

        public WorkspaceCatalog Catalog
        {
            get;
        }

        public void Dispose() => Directory.Delete(recursive: true);
    }
}
