using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TypeScriptSourceDiscovererTests
{
    [Fact]
    public async Task WhenDiscoveringTarget_ThenFiltersIgnoredDirectoriesAndKeepsTypeScriptFiles()
    {
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace.Setup(candidate => candidate.GetRequiredTargetDirectoryPath("/repo/SharpSense.sln"))
            .Returns("/repo");
        repositoryWorkspace.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        fileDiscoverer.Setup(candidate => candidate.GetAllowedFiles(
                "/repo",
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(
            [
                new DiscoveredFile("/repo/src/App.ts", "src/App.ts"),
                new DiscoveredFile("/repo/src/App.tsx", "src/App.tsx"),
                new DiscoveredFile("/repo/src/build/App.ts", "src/build/App.ts"),
                new DiscoveredFile("/repo/src/dist/App.tsx", "src/dist/App.tsx"),
                new DiscoveredFile("/repo/src/node_modules/Generated.ts", "src/node_modules/Generated.ts"),
                new DiscoveredFile("/repo/src/Styles.css", "src/Styles.css")
            ]);
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/src/App.ts"] = new("export const value = 42;"),
            ["/repo/src/App.tsx"] = new("export function App() { return null; }")
        }, "/repo");
        var discoverer = new TypeScriptSourceDiscoverer(
            repositoryWorkspace.Object,
            fileDiscoverer.Object,
            fileSystem,
            new TsConfigResolver(repositoryWorkspace.Object, fileSystem));

        var result = await discoverer.Discover("/repo/SharpSense.sln", TestContext.Current.CancellationToken);

        result.Select(static file => file.RelativeFilePath)
            .Should()
            .Equal("src/App.ts", "src/App.tsx");
    }

    [Fact]
    public async Task WhenDiscoveringRequestedFiles_ThenReturnsOnlyMatchingExistingTypeScriptFiles()
    {
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace.Setup(candidate => candidate.GetRequiredTargetDirectoryPath("/repo/SharpSense.sln"))
            .Returns("/repo");
        repositoryWorkspace.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        fileDiscoverer.Setup(candidate => candidate.GetAllowedFiles(
                "/repo",
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(
            [
                new DiscoveredFile("/repo/src/App.ts", "src/App.ts"),
                new DiscoveredFile("/repo/src/App.tsx", "src/App.tsx"),
                new DiscoveredFile("/repo/src/build/App.tsx", "src/build/App.tsx")
            ]);
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/src/App.ts"] = new("export const value = 42;"),
            ["/repo/src/App.tsx"] = new("export function App() { return null; }")
        }, "/repo");
        var discoverer = new TypeScriptSourceDiscoverer(
            repositoryWorkspace.Object,
            fileDiscoverer.Object,
            fileSystem,
            new TsConfigResolver(repositoryWorkspace.Object, fileSystem));

        var result = await discoverer.DiscoverFiles(
            "/repo/SharpSense.sln",
            ["/repo/src/App.tsx", "src/Unknown.ts", "/repo/src/build/App.tsx"],
            TestContext.Current.CancellationToken);

        result.Select(static file => file.RelativeFilePath)
            .Should()
            .Equal("src/App.tsx");
    }

    [Fact]
    public async Task WhenDiscoveringWorkspacePackageBarrels_ThenItFollowsExportFromDependencies()
    {
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace.Setup(candidate => candidate.GetRequiredTargetDirectoryPath("/repo/apps/web/tsconfig.json"))
            .Returns("/repo/apps/web");
        repositoryWorkspace.SetupGet(candidate => candidate.RootPath)
            .Returns("/repo");
        repositoryWorkspace.Setup(candidate => candidate.IsSameOrSubPath(It.IsAny<string>()))
            .Returns<string>(path => path.StartsWith("/repo", StringComparison.Ordinal));
        repositoryWorkspace.Setup(candidate => candidate.ToRepositoryRelativePath(It.IsAny<string>()))
            .Returns<string>(path => path["/repo/".Length..]);
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        fileDiscoverer.Setup(candidate => candidate.GetAllowedFiles(
                "/repo/apps/web",
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(
            [
                new DiscoveredFile("/repo/apps/web/src/App.tsx", "apps/web/src/App.tsx")
            ]);
        fileDiscoverer.Setup(candidate => candidate.GetAllowedFiles(
                "/repo",
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                TestContext.Current.CancellationToken))
            .ReturnsAsync(
            [
                new DiscoveredFile("/repo/apps/web/src/App.tsx", "apps/web/src/App.tsx"),
                new DiscoveredFile("/repo/packages/ai-chat-api/src/query-hooks/index.ts", "packages/ai-chat-api/src/query-hooks/index.ts"),
                new DiscoveredFile("/repo/packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts", "packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts")
            ]);
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/apps/web/src/App.tsx"] = new(
                """
                import { useGetConversationById } from "@loanmarket/ai-chat-api/query-hooks";

                export function App() {
                    return useGetConversationById();
                }
                """),
            ["/repo/apps/web/tsconfig.json"] = new("{}"),
            ["/repo/packages/ai-chat-api/package.json"] = new(
                """
                {
                  "name": "@loanmarket/ai-chat-api"
                }
                """),
            ["/repo/packages/ai-chat-api/tsconfig.json"] = new(
                """
                {
                  "compilerOptions": {
                    "baseUrl": "src"
                  }
                }
                """),
            ["/repo/packages/ai-chat-api/src/query-hooks/index.ts"] = new(
                """
                export { useGetConversationById } from "./use-get-conversation-by-id-query.g";
                """),
            ["/repo/packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts"] = new(
                """
                export function useGetConversationById() {
                    return null;
                }
                """)
        }, "/repo");
        var discoverer = new TypeScriptSourceDiscoverer(
            repositoryWorkspace.Object,
            fileDiscoverer.Object,
            fileSystem,
            new TsConfigResolver(repositoryWorkspace.Object, fileSystem));

        var result = await discoverer.Discover("/repo/apps/web/tsconfig.json", TestContext.Current.CancellationToken);

        result.Select(static file => file.RelativeFilePath)
            .Should()
            .Equal(
                "apps/web/src/App.tsx",
                "packages/ai-chat-api/src/query-hooks/index.ts",
                "packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts");
    }
}
