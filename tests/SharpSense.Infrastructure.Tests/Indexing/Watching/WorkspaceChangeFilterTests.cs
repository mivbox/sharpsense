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
    public void DeclaredGeneratorInputsAndNewMarkdownMembershipInvalidateCSharpOutsideDocumentationSelections()
    {
        var source = new WorkspaceSource(WorkspaceSourceKind.CSharp, "app/App.csproj");
        var filter = Create(source, new(WorkspaceSourceKind.Markdown, "docs/**/*.md"));
        filter.TrackSource(source, new([], [], [], [], ["/repo/schemas/input.md", "/repo/schemas/input.txt"]));

        Assert.True(filter.IsRelevant([Modified("schemas/input.md")]));
        Assert.True(filter.IsRelevant([Modified("schemas/input.txt")]));
        Assert.True(filter.IsRelevant([new(WorkspaceFileChangeAction.Deleted, OldPath: "/repo/schemas/input.md")]));
        Assert.True(filter.IsRelevant([new(WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/repo/schemas")]));
        Assert.True(filter.IsRelevant([Added("schemas/new.md")]));
        Assert.True(filter.IsRelevant([new(WorkspaceFileChangeAction.Renamed, OldPath: "/repo/other.md", NewPath: "/repo/schemas/new.md")]));
        Assert.False(filter.IsRelevant([Modified("unselected/existing.md")]));
    }

    [Fact]
    public void CSharpSelectionTracksReferencedProjectsWithoutWatchingUnrelatedProjects()
    {
        var source = new WorkspaceSource(WorkspaceSourceKind.CSharp, "app/App.csproj");
        var filter = Create(source);
        filter.TrackSource(source, new ExtractedNodes(
            [new IndexedProject("dependency", "Dependency", "shared/Shared.csproj", "hash")],
            [Node("shared/Service.cs"), Node("linked/SharedFile.cs")], [], [], ["/repo/linked/InitiallyEmpty.cs"]));

        Assert.True(filter.IsRelevant([Modified("app/Feature.cs")]));
        Assert.True(filter.IsRelevant([Modified("shared/Service.cs")]));
        Assert.True(filter.IsRelevant([Added("shared/NewService.cs")]));
        Assert.True(filter.IsRelevant([Modified("linked/SharedFile.cs")]));
        Assert.True(filter.IsRelevant([Modified("linked/InitiallyEmpty.cs")]));
        Assert.True(filter.IsRelevant([Modified("build/custom.targets")]));
        Assert.False(filter.IsRelevant([Modified("unrelated/Other.cs")]));
        Assert.False(filter.IsRelevant([Modified("frontend/index.ts")]));
        Assert.False(filter.IsRelevant([Modified("app/obj/Generated.cs")]));
    }

    [Fact]
    public void DeclaredInputCaseVariantsRemainRelevantWithoutBroadeningRepositoryOrSourceScope()
    {
        var source = new WorkspaceSource(WorkspaceSourceKind.CSharp, "app/App.csproj");
        var filter = Create(source);
        filter.TrackSource(source, new([], [], [], [], ["/repo/schemas/Input.md", "/repo/schemas/Input.txt"]));

        Assert.True(filter.IsRelevant([Modified("SCHEMAS/input.md")]));
        Assert.True(filter.IsRelevant([Modified("SCHEMAS/input.txt")]));
        Assert.True(filter.IsRelevant([new(WorkspaceFileChangeAction.Deleted, OldPath: "/repo/SCHEMAS/input.md")]));
        Assert.True(filter.IsRelevant([new(WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/repo/SCHEMAS")]));
        Assert.False(filter.IsRelevant([Modified("schemas/Other.md")]));
        Assert.False(filter.IsRelevant([new(WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/repo/schemas-other")]));
        Assert.False(filter.IsRelevant([new(WorkspaceFileChangeAction.Modified, NewPath: "/other/schemas/Input.md")]));
        Assert.False(filter.IsRelevant([new(WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/other/schemas")]));
    }

    [Fact]
    public void TypeScriptSelectionIncludesImportedFilesAndSharedConfiguration()
    {
        var source = new WorkspaceSource(WorkspaceSourceKind.TypeScript, "frontend/tsconfig.json");
        var filter = Create(source);
        filter.TrackSource(source, new ExtractedNodes([], [Node("shared/format.ts")], [], [], ["/repo/shared/later.ts"]));

        Assert.True(filter.IsRelevant([Modified("frontend/view.tsx")]));
        Assert.True(filter.IsRelevant([Modified("shared/format.ts")]));
        Assert.True(filter.IsRelevant([Added("shared/later.ts")]));
        Assert.True(filter.IsRelevant([Modified("configs/base.json")]));
        Assert.False(filter.IsRelevant([Modified("unrelated/other.ts")]));
        Assert.False(filter.IsRelevant([Modified("app/Feature.cs")]));
    }

    [Fact]
    public void DocumentationGlobsAreRepositoryRelativeAndTrackOldRenamePaths()
    {
        var filter = Create(new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/architecture/**/*.md"));

        Assert.True(filter.IsRelevant([Modified("docs/architecture/overview.md")]));
        Assert.False(filter.IsRelevant([Modified("docs/product/overview.md")]));
        Assert.True(filter.IsRelevant([new WorkspaceFileChange(
            WorkspaceFileChangeAction.Renamed,
            OldPath: "/repo/docs/architecture/overview.md",
            NewPath: "/repo/archive/overview.md")]));
        Assert.True(filter.IsRelevant([new WorkspaceFileChange(
            WorkspaceFileChangeAction.DirectoryDeleted, OldPath: "/repo/docs/architecture")]));
        Assert.False(filter.IsRelevant([new WorkspaceFileChange(
            WorkspaceFileChangeAction.Modified, NewPath: "/other/docs/architecture/overview.md")]));
    }

    [Fact]
    public void MultipleSourceKindsShareOneChangeFilter()
    {
        var filter = Create(
            new WorkspaceSource(WorkspaceSourceKind.CSharp, "app/App.csproj"),
            new WorkspaceSource(WorkspaceSourceKind.TypeScript, "frontend"),
            new WorkspaceSource(WorkspaceSourceKind.Markdown, "docs/**/*.md"));

        Assert.True(filter.IsRelevant([Modified("app/Feature.cs")]));
        Assert.True(filter.IsRelevant([Modified("frontend/index.ts")]));
        Assert.True(filter.IsRelevant([Modified("docs/guide.md")]));
        Assert.False(filter.IsRelevant([Modified("unselected/index.ts")]));
    }

    private static WorkspaceChangeFilter Create(params WorkspaceSource[] sources)
        => new(new WorkspacePaths(), Options.Create(new SharpSenseCliOptions { WorkspaceSources = sources }));

    private static WorkspaceFileChange Modified(string path) => new(WorkspaceFileChangeAction.Modified, NewPath: $"/repo/{path}");
    private static WorkspaceFileChange Added(string path) => new(WorkspaceFileChangeAction.Added, NewPath: $"/repo/{path}");

    private static IndexedCodeNode Node(string path)
        => new(path, null, path, path, NodeType.Method, path, 1, 2, "", path);

    private sealed class WorkspacePaths : IIndexingWorkspacePaths
    {
        public string RootPath => "/repo";
        public string GetRequiredTargetPath(string targetPath) => Path.GetFullPath(targetPath, RootPath);
        public string ToRepositoryRelativePath(string? filePath) => Path.GetRelativePath(RootPath, GetRequiredTargetPath(filePath!));

        public bool TryToRepositoryRelativePath(string? filePath, out string relativePath)
        {
            relativePath = ToRepositoryRelativePath(filePath);
            return !relativePath.StartsWith("..", StringComparison.Ordinal);
        }
    }
}
