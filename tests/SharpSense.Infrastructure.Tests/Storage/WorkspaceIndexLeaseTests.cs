using System.IO.Abstractions;
using SharpSense.Application.Indexing;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Storage;

public sealed class WorkspaceIndexLeaseTests
{
    [Fact]
    public void IndependentCatalogsCannotIndexOrChangeSourcesWhileLeaseIsHeld()
    {
        using var fixture = new Fixture();
        var first = fixture.Catalog;
        var second = new WorkspaceCatalog(new FileSystem(), fixture.Home);
        var selection = first.Create("product", fixture.RepositoryRoot, [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var original = File.ReadAllText(selection.ConfigurationPath);
        using var lease = first.AcquireIndexLease(selection);

        var busy = Assert.Throws<WorkspaceIndexBusyException>(() => second.AcquireIndexLease(second.Resolve("product", fixture.RepositoryRoot)));
        Assert.Contains("Stop that session", busy.Message);
        Assert.Throws<WorkspaceIndexBusyException>(() => second.AddSources("product", [new(WorkspaceSourceKind.Markdown, "notes/**/*.md")]));
        Assert.Throws<WorkspaceIndexBusyException>(() => second.RemoveSources("product", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]));
        Assert.Throws<WorkspaceIndexBusyException>(() => second.Update("product", "renamed", []));

        Assert.Equal(original, File.ReadAllText(selection.ConfigurationPath));
    }

    [Fact]
    public void ReleasedLeaseCanBeAcquiredAgainWithoutDeletingLockFile()
    {
        using var fixture = new Fixture();
        var selection = fixture.Catalog.Create("product", fixture.RepositoryRoot, []);
        var lease = fixture.Catalog.AcquireIndexLease(selection);

        lease.Dispose();
        lease.Dispose();

        Assert.True(File.Exists(Path.Combine(selection.DirectoryPath, ".index.lock")));
        var other = new WorkspaceCatalog(new FileSystem(), fixture.Home);
        using (other.AcquireIndexLease(other.Resolve("product", fixture.RepositoryRoot)))
        {
            Assert.Throws<WorkspaceIndexBusyException>(() => fixture.Catalog.AcquireIndexLease(selection));
        }

        var updated = fixture.Catalog.AddSources("product", [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        Assert.Single(updated.Definition.Sources);
    }

    [Fact]
    public void StaleSelectionCannotAcquireWriterLeaseAfterSourcesChange()
    {
        using var fixture = new Fixture();
        var stale = fixture.Catalog.Create("product", fixture.RepositoryRoot, [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var current = fixture.Catalog.AddSources("product", [new(WorkspaceSourceKind.Markdown, "notes/**/*.md")]);

        var exception = Assert.Throws<WorkspaceDefinitionChangedException>(() => fixture.Catalog.AcquireIndexLease(stale));

        Assert.Contains("Restart indexing or watching", exception.Message);
        using var lease = fixture.Catalog.AcquireIndexLease(current);
    }

    [Fact]
    public void DifferentWorkspacesCanBeIndexedConcurrentlyAndMergedIntoIndependentSelection()
    {
        using var fixture = new Fixture();
        var first = fixture.Catalog.Create("first", fixture.RepositoryRoot, [new(WorkspaceSourceKind.Markdown, "docs/**/*.md")]);
        var second = fixture.Catalog.Create("second", fixture.RepositoryRoot, [new(WorkspaceSourceKind.Markdown, "notes/**/*.md")]);
        using var firstLease = fixture.Catalog.AcquireIndexLease(first);
        using var secondLease = fixture.Catalog.AcquireIndexLease(second);

        var merged = fixture.Catalog.Merge("combined", ["first", "second"]);

        Assert.Equal(2, merged.Definition.Sources.Length);
        using var mergedLease = fixture.Catalog.AcquireIndexLease(merged);
    }

    [Fact]
    public void SelectionFromAnotherHomeCannotLockUnrelatedStorage()
    {
        using var fixture = new Fixture();
        var selection = fixture.Catalog.Create("product", fixture.RepositoryRoot, []);
        var differentHome = Path.Combine(fixture.Directory.FullName, "different-home");
        var otherCatalog = new WorkspaceCatalog(new FileSystem(), differentHome);

        Assert.Throws<WorkspaceDefinitionChangedException>(() => otherCatalog.AcquireIndexLease(selection));

        Assert.False(System.IO.Directory.Exists(differentHome));
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

        public DirectoryInfo Directory { get; }

        public string RepositoryRoot { get; }

        public string Home { get; }

        public WorkspaceCatalog Catalog { get; }

        public void Dispose() => Directory.Delete(recursive: true);
    }
}
