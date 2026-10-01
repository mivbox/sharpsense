using AwesomeAssertions;
using Microsoft.Extensions.Options;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing.Watching;

namespace SharpSense.Infrastructure.Tests.Indexing.Watching;

public sealed class WorkspaceChangeFilterTests
{
    [Fact]
    public void WhenGeneratorInputsOrMarkdownMembershipChange_ThenCSharpIsInvalidatedOutsideDocumentationSelections()
    {
        var source = new WorkspaceSource(WorkspaceSourceKind.CSharp, "app/App.csproj");
        var filter = Create(source, new(WorkspaceSourceKind.Markdown, "docs/**/*.md"));
        filter.TrackSource(source, new([], [], [], [], ["/repo/schemas/input.md", "/repo/schemas/input.txt"]));

        filter.IsRelevant([Modified("schemas/input.md")]).Should().BeTrue();
        filter.IsRelevant([Modified("schemas/input.txt")]).Should().BeTrue();
        filter.IsRelevant([new(WorkspaceFileChangeAction.Deleted, OldPath: "/repo/schemas/input.md")]).Should().BeTrue();
        filter.IsRelevant([new(WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/repo/schemas")]).Should().BeTrue();
        filter.IsRelevant([Added("schemas/new.md")]).Should().BeTrue();
        filter.IsRelevant(
            [
                new(
                    WorkspaceFileChangeAction.Renamed,
                    OldPath: "/repo/other.md",
                    NewPath: "/repo/schemas/new.md")
            ]).Should().BeTrue();
        filter.IsRelevant([Modified("unselected/existing.md")]).Should().BeFalse();
    }

    [Fact]
    public void WhenCSharpSelection_ThenTracksReferencedProjectsWithoutWatchingUnrelatedProjects()
    {
        var source = new WorkspaceSource(WorkspaceSourceKind.CSharp, "app/App.csproj");
        var filter = Create(source);
        filter.TrackSource(
            source,
            new ExtractedNodes(
                [new IndexedProject("dependency", "Dependency", "shared/Shared.csproj", "hash")],
                [Node("shared/Service.cs"), Node("linked/SharedFile.cs")],
                [],
                [],
                ["/repo/linked/InitiallyEmpty.cs"]));

        filter.IsRelevant([Modified("app/Feature.cs")]).Should().BeTrue();
        filter.IsRelevant([Modified("shared/Service.cs")]).Should().BeTrue();
        filter.IsRelevant([Added("shared/NewService.cs")]).Should().BeTrue();
        filter.IsRelevant([Modified("linked/SharedFile.cs")]).Should().BeTrue();
        filter.IsRelevant([Modified("linked/InitiallyEmpty.cs")]).Should().BeTrue();
        filter.IsRelevant([Modified("build/custom.targets")]).Should().BeTrue();
        filter.IsRelevant([Modified("unrelated/Other.cs")]).Should().BeFalse();
        filter.IsRelevant([Modified("frontend/index.ts")]).Should().BeFalse();
        filter.IsRelevant([Modified("app/obj/Generated.cs")]).Should().BeFalse();
    }

    [Fact]
    public void WhenDeclaredInputCaseVariants_ThenRemainRelevantWithoutBroadeningRepositoryOrSourceScope()
    {
        var source = new WorkspaceSource(WorkspaceSourceKind.CSharp, "app/App.csproj");
        var filter = Create(source);
        filter.TrackSource(source, new([], [], [], [], ["/repo/schemas/Input.md", "/repo/schemas/Input.txt"]));

        filter.IsRelevant([Modified("SCHEMAS/input.md")]).Should().BeTrue();
        filter.IsRelevant([Modified("SCHEMAS/input.txt")]).Should().BeTrue();
        filter.IsRelevant([new(WorkspaceFileChangeAction.Deleted, OldPath: "/repo/SCHEMAS/input.md")]).Should().BeTrue();
        filter.IsRelevant([new(WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/repo/SCHEMAS")]).Should().BeTrue();
        filter.IsRelevant([Modified("schemas/Other.md")]).Should().BeFalse();
        filter.IsRelevant([new(WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/repo/schemas-other")]).Should().BeFalse();
        filter.IsRelevant([new(WorkspaceFileChangeAction.Modified, NewPath: "/other/schemas/Input.md")]).Should().BeFalse();
        filter.IsRelevant([new(WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/other/schemas")]).Should().BeFalse();
    }

    [Fact]
    public void WhenTypeScriptSelection_ThenIncludesImportedFilesAndSharedConfiguration()
    {
        var source = new WorkspaceSource(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json");
        var filter = Create(source);
        filter.TrackSource(
            source,
            new ExtractedNodes(
                [],
                [Node("shared/format.ts")],
                [],
                [],
                ["/repo/shared/later.ts"]));

        filter.IsRelevant([Modified("frontend/view.tsx")]).Should().BeTrue();
        filter.IsRelevant([Modified("shared/format.ts")]).Should().BeTrue();
        filter.IsRelevant([Added("shared/later.ts")]).Should().BeTrue();
        filter.IsRelevant([Modified("configs/base.json")]).Should().BeTrue();
        filter.IsRelevant([Modified("unrelated/other.ts")]).Should().BeFalse();
        filter.IsRelevant([Modified("app/Feature.cs")]).Should().BeFalse();
    }

    [Fact]
    public void WhenDocumentationGlobs_ThenAreRepositoryRelativeAndTrackOldRenamePaths()
    {
        var filter = Create(new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/architecture/**/*.md"));

        filter.IsRelevant([Modified("docs/architecture/overview.md")]).Should().BeTrue();
        filter.IsRelevant([Modified("docs/product/overview.md")]).Should().BeFalse();
        filter.IsRelevant(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Renamed,
                    OldPath: "/repo/docs/architecture/overview.md",
                    NewPath: "/repo/archive/overview.md")
            ]).Should().BeTrue();
        filter.IsRelevant(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.DirectoryDeleted,
                    OldPath: "/repo/docs/architecture")
            ]).Should().BeTrue();
        filter.IsRelevant(
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/other/docs/architecture/overview.md")
            ]).Should().BeFalse();
    }

    [Fact]
    public void WhenMultipleSourceKinds_ThenShareOneChangeFilter()
    {
        var filter = Create(
            new WorkspaceSource(WorkspaceSourceKind.CSharp, "app/App.csproj"),
            new WorkspaceSource(WorkspaceSourceKind.TypeScript, "frontend"),
            new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md"));

        filter.IsRelevant([Modified("app/Feature.cs")]).Should().BeTrue();
        filter.IsRelevant([Modified("frontend/index.ts")]).Should().BeTrue();
        filter.IsRelevant([Modified("docs/guide.md")]).Should().BeTrue();
        filter.IsRelevant([Modified("unselected/index.ts")]).Should().BeFalse();
    }

    private static WorkspaceChangeFilter Create(params WorkspaceSource[] sources)
        => new(
            new WorkspacePaths(),
            Options.Create(new WorkspaceExecutionOptions
            {
                WorkspaceSources = sources
            }));

    private static WorkspaceFileChange Modified(string path) => new(
        WorkspaceFileChangeAction.Modified,
        NewPath: $"/repo/{path}");
    private static WorkspaceFileChange Added(string path) => new(
        WorkspaceFileChangeAction.Added,
        NewPath: $"/repo/{path}");

    private static IndexedCodeNode Node(string path)
        => new(path, null, path, path, NodeType.Method, path, 1, 2, "", path);

    private sealed class WorkspacePaths : IIndexingWorkspacePaths
    {
        public string RootPath => "/repo";
        public string GetRequiredTargetPath(string targetPath) => Path.GetFullPath(targetPath, RootPath);
        public string ToRepositoryRelativePath(string? filePath) => Path.GetRelativePath(
            RootPath,
            GetRequiredTargetPath(filePath!));

        public bool TryToRepositoryRelativePath(string? filePath, out string relativePath)
        {
            relativePath = ToRepositoryRelativePath(filePath);

            return !relativePath.StartsWith("..", StringComparison.Ordinal);
        }
    }
}
