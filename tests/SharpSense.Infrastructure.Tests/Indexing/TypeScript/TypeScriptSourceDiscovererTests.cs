using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;
using static SharpSense.Infrastructure.Tests.Indexing.TypeScript.TypeScriptTestPaths;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TypeScriptSourceDiscovererTests
{
    [Fact]
    public async Task WhenDiscoveringTarget_ThenFiltersIgnoredDirectoriesAndKeepsTypeScriptFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace
            .Setup(candidate => candidate.GetRequiredTargetDirectoryPath(Absolute("/repo/SharpSense.sln")))
            .Returns(Absolute("/repo"));
        repositoryWorkspace
            .SetupGet(candidate => candidate.RootPath)
            .Returns(Absolute("/repo"));
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        fileDiscoverer
            .Setup(candidate => candidate.GetAllowedFiles(
                Absolute("/repo"),
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                ct))
            .ReturnsAsync(
                [
                    new DiscoveredFile(Absolute("/repo/src/App.ts"), "src/App.ts"),
                    new DiscoveredFile(Absolute("/repo/src/App.tsx"), "src/App.tsx"),
                    new DiscoveredFile(Absolute("/repo/src/build/App.ts"), "src/build/App.ts"),
                    new DiscoveredFile(Absolute("/repo/src/dist/App.tsx"), "src/dist/App.tsx"),
                    new DiscoveredFile(Absolute("/repo/src/node_modules/Generated.ts"), "src/node_modules/Generated.ts"),
                    new DiscoveredFile(Absolute("/repo/src/Styles.css"), "src/Styles.css")
                ]);
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                [Absolute("/repo/src/App.ts")] = new("export const value = 42;"),
                [Absolute("/repo/src/App.tsx")] = new("export function App() { return null; }")
            },
            Absolute("/repo"));
        var discoverer = new TypeScriptSourceDiscoverer(
            repositoryWorkspace.Object,
            fileDiscoverer.Object,
            fileSystem,
            new TsConfigResolver(repositoryWorkspace.Object, fileSystem));

        var result = await discoverer.Discover(Absolute("/repo/SharpSense.sln"), ct);

        result
            .Select(static file => file.DiscoveredFile.RelativeFilePath)
            .Should()
            .Equal("src/App.ts", "src/App.tsx");
    }

    [Fact]
    public async Task WhenDiscoveringWorkspacePackageBarrels_ThenItFollowsExportFromDependencies()
    {
        var ct = TestContext.Current.CancellationToken;
        var repositoryWorkspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        repositoryWorkspace
            .Setup(candidate => candidate.GetRequiredTargetDirectoryPath(Absolute("/repo/apps/web/tsconfig.json")))
            .Returns(Absolute("/repo/apps/web"));
        repositoryWorkspace
            .SetupGet(candidate => candidate.RootPath)
            .Returns(Absolute("/repo"));
        repositoryWorkspace
            .Setup(candidate => candidate.IsSameOrSubPath(It.IsAny<string>()))
            .Returns<string>(path => path.StartsWith(Absolute("/repo"), StringComparison.Ordinal));
        repositoryWorkspace
            .Setup(candidate => candidate.ToRepositoryRelativePath(It.IsAny<string>()))
            .Returns<string>(path => path[Absolute("/repo/").Length..].Replace('\\', '/'));
        var fileDiscoverer = new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict);
        fileDiscoverer
            .Setup(candidate => candidate.GetAllowedFiles(
                Absolute("/repo/apps/web"),
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                ct))
            .ReturnsAsync(
                [
                    new DiscoveredFile(Absolute("/repo/apps/web/src/App.tsx"), "apps/web/src/App.tsx")
                ]);
        fileDiscoverer
            .Setup(candidate => candidate.GetAllowedFiles(
                Absolute("/repo"),
                It.Is<IReadOnlyList<string>>(globs => globs.SequenceEqual(TypeScriptIndexingPathRules.IncludeGlobs)),
                ct))
            .ReturnsAsync(
                [
                    new DiscoveredFile(Absolute("/repo/apps/web/src/App.tsx"), "apps/web/src/App.tsx"),
                    new DiscoveredFile(
                        Absolute("/repo/packages/graph-api/src/query-hooks/index.ts"),
                        "packages/graph-api/src/query-hooks/index.ts"),
                    new DiscoveredFile(
                        Absolute("/repo/packages/graph-api/src/query-hooks/use-get-node-by-id-query.g.ts"),
                        "packages/graph-api/src/query-hooks/use-get-node-by-id-query.g.ts")
                ]);
        var fileSystem = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                [Absolute("/repo/apps/web/src/App.tsx")] = new(
                    """
                import { useGetNodeById } from "@sharpsense/graph-api/query-hooks";

                export function App() {
                    return useGetNodeById();
                }
                """),
                [Absolute("/repo/apps/web/tsconfig.json")] = new("{}"),
                [Absolute("/repo/packages/graph-api/package.json")] = new(
                    """
                {
                  "name": "@sharpsense/graph-api"
                }
                """),
                [Absolute("/repo/packages/graph-api/tsconfig.json")] = new(
                    """
                {
                  "compilerOptions": {
                    "baseUrl": "src"
                  }
                }
                """),
                [Absolute("/repo/packages/graph-api/src/query-hooks/index.ts")] = new(
                    """
                export { useGetNodeById } from "./use-get-node-by-id-query.g";
                """),
                [Absolute("/repo/packages/graph-api/src/query-hooks/use-get-node-by-id-query.g.ts")] = new(
                    """
                export function useGetNodeById() {
                    return null;
                }
                """)
            },
            Absolute("/repo"));
        var discoverer = new TypeScriptSourceDiscoverer(
            repositoryWorkspace.Object,
            fileDiscoverer.Object,
            fileSystem,
            new TsConfigResolver(repositoryWorkspace.Object, fileSystem));

        var result = await discoverer.Discover(Absolute("/repo/apps/web/tsconfig.json"), ct);

        result
            .Select(static file => file.DiscoveredFile.RelativeFilePath)
            .Should()
            .Equal(
                "apps/web/src/App.tsx",
                "packages/graph-api/src/query-hooks/index.ts",
                "packages/graph-api/src/query-hooks/use-get-node-by-id-query.g.ts");
    }
}
