using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using static SharpSense.Infrastructure.Tests.Indexing.TypeScript.TypeScriptTestPaths;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TsConfigResolverTests
{
    [Fact]
    public void WhenCancelledPackageTraversal_ThenDoesNotPublishPartialResolverCaches()
    {
        var ct = TestContext.Current.CancellationToken;
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/tsconfig.json"), "{}"),
            (Absolute("/repo/app.ts"), ""),
            (Absolute("/repo/packages/first/package.json"), """{"name":"@fixture/first"}"""),
            (Absolute("/repo/packages/first/index.ts"), "export const first = 1;"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var cancelOnce = true;
        var directory = new Mock<IDirectory>();
        directory
            .Setup(value => value.Exists(It.IsAny<string>()))
            .Returns((string path) => fileSystem.Directory.Exists(path));
        directory
            .Setup(value => value.EnumerateDirectories(It.IsAny<string>()))
            .Returns((string path) =>
            {
                if (cancelOnce && path == Absolute("/repo/packages/first"))
                {
                    cancelOnce = false;
                    cancellation.Cancel();
                }

                return fileSystem.Directory.EnumerateDirectories(path);
            });
        var wrapper = new Mock<IFileSystem>();
        wrapper
            .SetupGet(value => value.Path)
            .Returns(fileSystem.Path);
        wrapper
            .SetupGet(value => value.File)
            .Returns(fileSystem.File);
        wrapper
            .SetupGet(value => value.Directory)
            .Returns(directory.Object);
        wrapper
            .SetupGet(value => value.DirectoryInfo)
            .Returns(fileSystem.DirectoryInfo);
        var resolver = new TsConfigResolver(
            new RepositoryWorkspace(Absolute("/repo"), Absolute("/test-storage/index.db"), fileSystem),
            wrapper.Object);

        ((Action)(() => resolver.ResolveImport("@fixture/first", Absolute("/repo/app.ts"), cancellation.Token))).Should().Throw<OperationCanceledException>();
        resolver.ResolutionInputPaths.Should().BeEmpty();
        fileSystem.AddFile(
            Absolute("/repo/packages/later/package.json"),
            new MockFileData("""{"name":"@fixture/later"}"""));
        fileSystem.AddFile(Absolute("/repo/packages/later/index.ts"), new MockFileData("export const later = 2;"));

        resolver
            .ResolveImport("@fixture/later", Absolute("/repo/app.ts"), ct)
            .Should()
            .Be(Absolute("/repo/packages/later/index.ts"));
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("dist")]
    [InlineData("build")]
    public void WhenIgnoredDirectories_ThenCannotRegisterWorkspacePackages(string directory)
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/app.ts"), ""),
            (Absolute($"/repo/{directory}/package.json"), """{"name":"@fixture/ignored"}"""),
            (Absolute($"/repo/{directory}/index.ts"), "export const ignored = 1;"));

        CreateResolver(fileSystem)
            .ResolveImport("@fixture/ignored", Absolute("/repo/app.ts"), TestContext.Current.CancellationToken)
            .Should()
            .BeNull();
    }

    [Fact]
    public void WhenUnresolvedImports_ThenRemainWatchInputsUntilTheirFilesAreCreated()
    {
        var fileSystem = CreateRepositoryFileSystem((Absolute("/repo/frontend/app.ts"), "import '../shared/later';"));
        var resolver = CreateResolver(fileSystem);

        resolver
            .ResolveImport(
                "../shared/later",
                Absolute("/repo/frontend/app.ts"),
                TestContext.Current.CancellationToken)
            .Should()
            .BeNull();

        resolver.ResolutionInputPaths.Should().Contain(Absolute("/repo/shared/later.ts"));
        resolver.ResolutionInputPaths.Should().Contain(Absolute("/repo/shared/later/index.ts"));
        resolver.ClearCache();
        resolver.ResolutionInputPaths.Should().BeEmpty();
    }

    [Fact]
    public void WhenConfigPatterns_ThenUseRepositoryCanonicalRootWhenTargetUsesAlias()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            [Absolute("/physical/repo/tsconfig.json")] = new("{}"),
            [Absolute("/physical/repo/app.ts")] = new("export function app() {}")
        });
        var workspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        workspace
            .SetupGet(candidate => candidate.RootPath)
            .Returns(Absolute("/physical/repo"));
        workspace
            .Setup(candidate => candidate.ToRepositoryRelativePath(Absolute("/alias/repo/tsconfig.json")))
            .Returns("tsconfig.json");
        var resolver = new TsConfigResolver(workspace.Object, fileSystem);
        DiscoveredFile[] files = [new(Absolute("/physical/repo/app.ts"), "app.ts")];

        resolver.FilterRootFiles(Absolute("/alias/repo/tsconfig.json"), files).Should().Equal(files);
    }

    [Fact]
    public void WhenRelativeConfigTargets_ThenResolveFromRepositoryInsteadOfProcessDirectory()
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/client/tsconfig.json"), "{}"),
            (Absolute("/repo/client/app.ts"), "export function app() {}"));
        fileSystem.Directory.SetCurrentDirectory(Absolute("/repo/client"));
        DiscoveredFile[] files = [new(Absolute("/repo/client/app.ts"), "client/app.ts")];

        CreateResolver(fileSystem)
            .FilterRootFiles("client/tsconfig.json", files)
            .Should()
            .Equal(files);
    }

    [Theory]
    [InlineData("{}", "/repo/shared/api.ts")]
    [InlineData("{ \"paths\": {} }", null)]
    public void WhenInheritedPaths_ThenRemainOnlyWhenDerivedPropertyIsOmitted(string compilerOptions, string? expected)
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/base.json"), """{ "compilerOptions": { "paths": { "@api": ["shared/api.ts"] } } }"""),
            (Absolute("/repo/client/tsconfig.json"), $$"""{ "extends": "../base.json", "compilerOptions": {{compilerOptions}} }"""),
            (Absolute("/repo/client/app.ts"), "export function app() {}"),
            (Absolute("/repo/shared/api.ts"), "export function api() {}"));

        CreateResolver(fileSystem)
            .ResolveImport("@api", Absolute("/repo/client/app.ts"), TestContext.Current.CancellationToken)
            .Should()
            .Be(expected is null ? null : Absolute(expected));
    }

    [Theory]
    [InlineData("{}", "/repo/shared/api.ts")]
    [InlineData("{ \"paths\": {} }", null)]
    public void WhenBaseConfigsAreLayered_ThenOnlyExplicitPathPropertiesOverrideEarlierOnes(
        string compilerOptions,
        string? expected)
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/base.json"), """{ "compilerOptions": { "paths": { "@api": ["shared/api.ts"] } } }"""),
            (Absolute("/repo/later.json"), $$"""{ "compilerOptions": {{compilerOptions}} }"""),
            (Absolute("/repo/client/tsconfig.json"), """{ "extends": ["../base.json", "../later.json"] }"""),
            (Absolute("/repo/client/app.ts"), "export function app() {}"),
            (Absolute("/repo/shared/api.ts"), "export function api() {}"));

        CreateResolver(fileSystem)
            .ResolveImport("@api", Absolute("/repo/client/app.ts"), TestContext.Current.CancellationToken)
            .Should()
            .Be(expected is null ? null : Absolute(expected));
    }

    [Fact]
    public void WhenDerivedConfigSpecifiesPaths_ThenItReplacesInheritedAliases()
    {
        var ct = TestContext.Current.CancellationToken;
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/tsconfig.base.json"),
                """
                {
                  // base config
                  "compilerOptions": {
                    "baseUrl": ".",
                    "paths": {
                      "@shared/*": ["src/shared/*",],
                    },
                  },
                }
                """),
            (Absolute("/repo/apps/web/tsconfig.json"),
                """
                {
                  "extends": "../../tsconfig.base",
                  "compilerOptions": {
                    "baseUrl": ".",
                    "paths": {
                      "@web/*": ["src/*"]
                    }
                  }
                }
                """),
            (Absolute("/repo/apps/web/src/components/App.tsx"), "export function App() { return null; }"),
            (Absolute("/repo/src/shared/api/client.ts"), "export const client = {};"),
            (Absolute("/repo/apps/web/src/lib/util.tsx"), "export function util() { return null; }"));
        var resolver = CreateResolver(fileSystem);
        var sourceFilePath = Absolute("/repo/apps/web/src/components/App.tsx");

        var sharedPath = resolver.ResolveImport("@shared/api/client", sourceFilePath, ct);
        var webPath = resolver.ResolveImport("@web/lib/util", sourceFilePath, ct);

        sharedPath.Should().BeNull();
        webPath.Should().Be(Absolute("/repo/apps/web/src/lib/util.tsx"));
    }

    [Fact]
    public void WhenResolveImportUsesRelativeSpecifier_ThenItFallsBackToTsxAndIndexFiles()
    {
        var ct = TestContext.Current.CancellationToken;
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/apps/web/tsconfig.json"),
                """
                {
                  "compilerOptions": {
                    "baseUrl": "."
                  }
                }
                """),
            (Absolute("/repo/apps/web/src/components/App.tsx"), "export function App() { return null; }"),
            (Absolute("/repo/apps/web/src/lib/http.tsx"), "export function http() { return null; }"),
            (Absolute("/repo/apps/web/src/routes/index.ts"), "export const route = {};"));
        var resolver = CreateResolver(fileSystem);
        var sourceFilePath = Absolute("/repo/apps/web/src/components/App.tsx");

        var tsxPath = resolver.ResolveImport("../lib/http", sourceFilePath, ct);
        var indexPath = resolver.ResolveImport("../routes", sourceFilePath, ct);

        tsxPath.Should().Be(Absolute("/repo/apps/web/src/lib/http.tsx"));
        indexPath.Should().Be(Absolute("/repo/apps/web/src/routes/index.ts"));
    }

    [Fact]
    public void WhenResolveImportUsesBaseUrlWithoutAlias_ThenItResolvesFromTheEffectiveBaseUrl()
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/apps/web/tsconfig.json"),
                """
                {
                  "compilerOptions": {
                    "baseUrl": "."
                  }
                }
                """),
            (Absolute("/repo/apps/web/src/components/App.tsx"), "export function App() { return null; }"),
            (Absolute("/repo/apps/web/src/services/api.ts"), "export const api = {};"));
        var resolver = CreateResolver(fileSystem);

        var resolvedPath = resolver.ResolveImport(
            "src/services/api",
            Absolute("/repo/apps/web/src/components/App.tsx"),
            TestContext.Current.CancellationToken);

        resolvedPath.Should().Be(Absolute("/repo/apps/web/src/services/api.ts"));
    }

    [Fact]
    public void WhenResolveImportUsesPackageExtends_ThenItLoadsConfigFromNodeModules()
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/node_modules/@tsconfig/strict/tsconfig.json"),
                """
                {
                  "compilerOptions": {
                    "baseUrl": "../../../",
                    "paths": {
                      "@shared/*": ["src/shared/*"]
                    }
                  }
                }
                """),
            (Absolute("/repo/apps/web/tsconfig.json"),
                """
                {
                  "extends": "@tsconfig/strict/tsconfig"
                }
                """),
            (Absolute("/repo/apps/web/src/components/App.tsx"), "export function App() { return null; }"),
            (Absolute("/repo/src/shared/api.ts"), "export const api = {};"));
        var resolver = CreateResolver(fileSystem);

        var resolvedPath = resolver.ResolveImport(
            "@shared/api",
            Absolute("/repo/apps/web/src/components/App.tsx"),
            TestContext.Current.CancellationToken);

        resolvedPath.Should().Be(Absolute("/repo/src/shared/api.ts"));
    }

    [Fact]
    public void WhenResolveImportUsesWorkspacePackageName_ThenItResolvesPackageSubpathsInsideTheRepository()
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/apps/web/tsconfig.json"),
                """
                {
                  "compilerOptions": {
                    "baseUrl": "."
                  }
                }
                """),
            (Absolute("/repo/apps/web/src/App.tsx"), "export function App() { return null; }"),
            (Absolute("/repo/packages/graph-api/package.json"),
                """
                {
                  "name": "@sharpsense/graph-api"
                }
                """),
            (Absolute("/repo/packages/graph-api/tsconfig.json"),
                """
                {
                  "compilerOptions": {
                    "baseUrl": "src"
                  }
                }
                """),
            (Absolute("/repo/packages/graph-api/src/query-hooks/use-get-node-by-id-query.g.ts"),
                "export function useGetNodeById() { return null; }"));
        var resolver = CreateResolver(fileSystem);

        var resolvedPath = resolver.ResolveImport(
            "@sharpsense/graph-api/query-hooks/use-get-node-by-id-query.g",
            Absolute("/repo/apps/web/src/App.tsx"),
            TestContext.Current.CancellationToken);

        resolvedPath.Should().Be(Absolute("/repo/packages/graph-api/src/query-hooks/use-get-node-by-id-query.g.ts"));
    }

    [Fact]
    public void WhenPathPatternsMatch_ThenLongestPrefixWinsBeforeWildcard()
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/tsconfig.json"), """
                { "compilerOptions": { "paths": { "@long/*": ["long/*"], "@*/suffix": ["short/*"] } } }
                """),
            (Absolute("/repo/app.ts"), ""),
            (Absolute("/repo/long/name/suffix.ts"), ""),
            (Absolute("/repo/short/long/name.ts"), ""));

        CreateResolver(fileSystem)
            .ResolveImport("@long/name/suffix", Absolute("/repo/app.ts"), TestContext.Current.CancellationToken)
            .Should()
            .Be(Absolute("/repo/long/name/suffix.ts"));
    }

    [Theory]
    [InlineData("@api", "@*")]
    [InlineData("@api/*", "@*")]
    public void WhenMissingBestPathMapping_ThenDoesNotFallThroughToLessSpecificKey(string preferred, string fallback)
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/tsconfig.json"), $$"""
                { "compilerOptions": { "paths": { "{{preferred}}": ["missing.ts"], "{{fallback}}": ["wrong.ts"] } } }
                """),
            (Absolute("/repo/app.ts"), ""),
            (Absolute("/repo/wrong.ts"), ""));

        CreateResolver(fileSystem)
            .ResolveImport(
                preferred.Replace("*", "value", StringComparison.Ordinal),
                Absolute("/repo/app.ts"),
                TestContext.Current.CancellationToken)
            .Should()
            .BeNull();
    }

    [Fact]
    public void WhenInheritedPathTargets_ThenUseDerivedBaseUrl()
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/base.json"), """{ "compilerOptions": { "baseUrl": ".", "paths": { "@/*": ["src/*"] } } }"""),
            (Absolute("/repo/app/tsconfig.json"), """{ "extends": "../base.json", "compilerOptions": { "baseUrl": "." } }"""),
            (Absolute("/repo/app/main.ts"), ""),
            (Absolute("/repo/src/dep.ts"), ""),
            (Absolute("/repo/app/src/dep.ts"), ""));

        CreateResolver(fileSystem)
            .ResolveImport("@/dep", Absolute("/repo/app/main.ts"), TestContext.Current.CancellationToken)
            .Should()
            .Be(Absolute("/repo/app/src/dep.ts"));
    }

    [Theory]
    [InlineData("./api", "/repo/api.d.ts")]
    [InlineData("./api.js", "/repo/api.d.ts")]
    [InlineData("./types", "/repo/types/index.d.ts")]
    public void WhenDeclarationFiles_ThenResolveAsModules(string import, string expected)
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/tsconfig.json"), "{}"),
            (Absolute("/repo/app.ts"), ""),
            (Absolute("/repo/api.d.ts"), "export declare function load(): void;"),
            (Absolute("/repo/types/index.d.ts"), "export interface Options {}"));

        CreateResolver(fileSystem)
            .ResolveImport(import, Absolute("/repo/app.ts"), TestContext.Current.CancellationToken)
            .Should()
            .Be(expected is null ? null : Absolute(expected));
    }

    [Theory]
    [InlineData(".ts")]
    [InlineData(".tsx")]
    public void WhenSourceModules_ThenRemainPreferredOverDeclarationFiles(string extension)
    {
        var fileSystem = CreateRepositoryFileSystem(
            (Absolute("/repo/tsconfig.json"), "{}"),
            (Absolute("/repo/app.ts"), ""),
            (Absolute($"/repo/api{extension}"), "export function load() {}"),
            (Absolute("/repo/api.d.ts"), "export declare function load(): void;"));

        CreateResolver(fileSystem)
            .ResolveImport("./api", Absolute("/repo/app.ts"), TestContext.Current.CancellationToken)
            .Should()
            .Be(Absolute($"/repo/api{extension}"));
    }

    private static TsConfigResolver CreateResolver(MockFileSystem fileSystem)
        => new(
            new RepositoryWorkspace(Absolute("/repo"), Absolute("/test-storage/index.db"), fileSystem),
            fileSystem);

    private static MockFileSystem CreateRepositoryFileSystem(params (string Path, string Content)[] files)
    {
        var fileData = new Dictionary<string, MockFileData>
        {
            [Absolute("/repo/.git/HEAD")] = new("ref: refs/heads/main")
        };

        foreach (var (path, content) in files)
        {
            fileData[path] = new MockFileData(content);
        }

        return new MockFileSystem(fileData, Absolute("/repo"));
    }
}
