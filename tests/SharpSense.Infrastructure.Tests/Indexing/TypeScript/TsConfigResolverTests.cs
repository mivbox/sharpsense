using AwesomeAssertions;
using Moq;
using SharpSense.Application.Indexing.Models;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TsConfigResolverTests
{
    [Fact]
    public void CancelledPackageTraversalDoesNotPublishPartialResolverCaches()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/tsconfig.json", "{}"), ("/repo/app.ts", ""),
            ("/repo/packages/first/package.json", """{"name":"@fixture/first"}"""),
            ("/repo/packages/first/index.ts", "export const first = 1;"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cancelOnce = true;
        var directory = new Mock<IDirectory>();
        directory.Setup(value => value.Exists(It.IsAny<string>()))
            .Returns((string path) => fileSystem.Directory.Exists(path));
        directory.Setup(value => value.EnumerateDirectories(It.IsAny<string>()))
            .Returns((string path) =>
            {
                if (cancelOnce && path == "/repo/packages/first")
                {
                    cancelOnce = false;
                    cancellation.Cancel();
                }
                return fileSystem.Directory.EnumerateDirectories(path);
            });
        var wrapper = new Mock<IFileSystem>();
        wrapper.SetupGet(value => value.Path).Returns(fileSystem.Path);
        wrapper.SetupGet(value => value.File).Returns(fileSystem.File);
        wrapper.SetupGet(value => value.Directory).Returns(directory.Object);
        wrapper.SetupGet(value => value.DirectoryInfo).Returns(fileSystem.DirectoryInfo);
        var resolver = new TsConfigResolver(new RepositoryWorkspace("/repo", "/test-storage/index.db", fileSystem), wrapper.Object);

        Assert.ThrowsAny<OperationCanceledException>(() => resolver.ResolveImport("@fixture/first", "/repo/app.ts", cancellation.Token));
        Assert.Empty(resolver.ResolutionInputPaths);
        fileSystem.AddFile("/repo/packages/later/package.json", new MockFileData("""{"name":"@fixture/later"}"""));
        fileSystem.AddFile("/repo/packages/later/index.ts", new MockFileData("export const later = 2;"));

        Assert.Equal("/repo/packages/later/index.ts", resolver.ResolveImport("@fixture/later", "/repo/app.ts", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("node_modules")]
    [InlineData("dist")]
    [InlineData("build")]
    public void IgnoredDirectoriesCannotRegisterWorkspacePackages(string directory)
    {
        var fileSystem = CreateRepositoryFileSystem(("/repo/app.ts", ""),
            ($"/repo/{directory}/package.json", """{"name":"@fixture/ignored"}"""),
            ($"/repo/{directory}/index.ts", "export const ignored = 1;"));

        Assert.Null(CreateResolver(fileSystem).ResolveImport("@fixture/ignored", "/repo/app.ts", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void UnresolvedImportsRemainWatchInputsUntilTheirFilesAreCreated()
    {
        var fileSystem = CreateRepositoryFileSystem(("/repo/frontend/app.ts", "import '../shared/later';"));
        var resolver = CreateResolver(fileSystem);

        resolver.ResolveImport("../shared/later", "/repo/frontend/app.ts", TestContext.Current.CancellationToken).Should().BeNull();

        resolver.ResolutionInputPaths.Should().Contain("/repo/shared/later.ts");
        resolver.ResolutionInputPaths.Should().Contain("/repo/shared/later/index.ts");
        resolver.ClearCache();
        resolver.ResolutionInputPaths.Should().BeEmpty();
    }

    [Fact]
    public void ConfigPatternsUseRepositoryCanonicalRootWhenTargetUsesAlias()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/physical/repo/tsconfig.json"] = new("{}"),
            ["/physical/repo/app.ts"] = new("export function app() {}")
        });
        var workspace = new Mock<IRepositoryWorkspace>(MockBehavior.Strict);
        workspace.SetupGet(candidate => candidate.RootPath).Returns("/physical/repo");
        workspace.Setup(candidate => candidate.ToRepositoryRelativePath("/alias/repo/tsconfig.json"))
            .Returns("tsconfig.json");
        var resolver = new TsConfigResolver(workspace.Object, fileSystem);
        DiscoveredFile[] files = [new("/physical/repo/app.ts", "app.ts")];

        resolver.FilterRootFiles("/alias/repo/tsconfig.json", files).Should().Equal(files);
    }

    [Fact]
    public void RelativeConfigTargetsResolveFromRepositoryInsteadOfProcessDirectory()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/client/tsconfig.json", "{}"),
            ("/repo/client/app.ts", "export function app() {}"));
        fileSystem.Directory.SetCurrentDirectory("/repo/client");
        DiscoveredFile[] files = [new("/repo/client/app.ts", "client/app.ts")];

        CreateResolver(fileSystem).FilterRootFiles("client/tsconfig.json", files).Should().Equal(files);
    }

    [Theory]
    [InlineData("{}", "/repo/shared/api.ts")]
    [InlineData("{ \"paths\": {} }", null)]
    public void InheritedPathsRemainOnlyWhenDerivedPropertyIsOmitted(string compilerOptions, string? expected)
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/base.json", """{ "compilerOptions": { "paths": { "@api": ["shared/api.ts"] } } }"""),
            ("/repo/client/tsconfig.json", $$"""{ "extends": "../base.json", "compilerOptions": {{compilerOptions}} }"""),
            ("/repo/client/app.ts", "export function app() {}"),
            ("/repo/shared/api.ts", "export function api() {}"));

        CreateResolver(fileSystem).ResolveImport("@api", "/repo/client/app.ts", TestContext.Current.CancellationToken).Should().Be(expected);
    }

    [Theory]
    [InlineData("{}", "/repo/shared/api.ts")]
    [InlineData("{ \"paths\": {} }", null)]
    public void LaterBaseConfigOverridesPathsOnlyWhenPropertyExists(string compilerOptions, string? expected)
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/base.json", """{ "compilerOptions": { "paths": { "@api": ["shared/api.ts"] } } }"""),
            ("/repo/later.json", $$"""{ "compilerOptions": {{compilerOptions}} }"""),
            ("/repo/client/tsconfig.json", """{ "extends": ["../base.json", "../later.json"] }"""),
            ("/repo/client/app.ts", "export function app() {}"),
            ("/repo/shared/api.ts", "export function api() {}"));

        CreateResolver(fileSystem).ResolveImport("@api", "/repo/client/app.ts", TestContext.Current.CancellationToken).Should().Be(expected);
    }

    [Fact]
    public void WhenDerivedConfigSpecifiesPaths_ThenItReplacesInheritedAliases()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/tsconfig.base.json",
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
            ("/repo/apps/web/tsconfig.json",
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
            ("/repo/apps/web/src/components/App.tsx", "export function App() { return null; }"),
            ("/repo/src/shared/api/client.ts", "export const client = {};"),
            ("/repo/apps/web/src/lib/util.tsx", "export function util() { return null; }"));
        var resolver = CreateResolver(fileSystem);
        const string sourceFilePath = "/repo/apps/web/src/components/App.tsx";

        var sharedPath = resolver.ResolveImport("@shared/api/client", sourceFilePath, TestContext.Current.CancellationToken);
        var webPath = resolver.ResolveImport("@web/lib/util", sourceFilePath, TestContext.Current.CancellationToken);

        sharedPath.Should().BeNull();
        webPath.Should().Be("/repo/apps/web/src/lib/util.tsx");
    }

    [Fact]
    public void WhenResolveImportUsesRelativeSpecifier_ThenItFallsBackToTsxAndIndexFiles()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/apps/web/tsconfig.json",
                """
                {
                  "compilerOptions": {
                    "baseUrl": "."
                  }
                }
                """),
            ("/repo/apps/web/src/components/App.tsx", "export function App() { return null; }"),
            ("/repo/apps/web/src/lib/http.tsx", "export function http() { return null; }"),
            ("/repo/apps/web/src/routes/index.ts", "export const route = {};"));
        var resolver = CreateResolver(fileSystem);
        const string sourceFilePath = "/repo/apps/web/src/components/App.tsx";

        var tsxPath = resolver.ResolveImport("../lib/http", sourceFilePath, TestContext.Current.CancellationToken);
        var indexPath = resolver.ResolveImport("../routes", sourceFilePath, TestContext.Current.CancellationToken);

        tsxPath.Should().Be("/repo/apps/web/src/lib/http.tsx");
        indexPath.Should().Be("/repo/apps/web/src/routes/index.ts");
    }

    [Fact]
    public void WhenResolveImportUsesBaseUrlWithoutAlias_ThenItResolvesFromTheEffectiveBaseUrl()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/apps/web/tsconfig.json",
                """
                {
                  "compilerOptions": {
                    "baseUrl": "."
                  }
                }
                """),
            ("/repo/apps/web/src/components/App.tsx", "export function App() { return null; }"),
            ("/repo/apps/web/src/services/api.ts", "export const api = {};"));
        var resolver = CreateResolver(fileSystem);

        var resolvedPath = resolver.ResolveImport(
            "src/services/api",
            "/repo/apps/web/src/components/App.tsx", TestContext.Current.CancellationToken);

        resolvedPath.Should().Be("/repo/apps/web/src/services/api.ts");
    }

    [Fact]
    public void WhenResolveImportUsesPackageExtends_ThenItLoadsConfigFromNodeModules()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/node_modules/@tsconfig/strict/tsconfig.json",
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
            ("/repo/apps/web/tsconfig.json",
                """
                {
                  "extends": "@tsconfig/strict/tsconfig"
                }
                """),
            ("/repo/apps/web/src/components/App.tsx", "export function App() { return null; }"),
            ("/repo/src/shared/api.ts", "export const api = {};"));
        var resolver = CreateResolver(fileSystem);

        var resolvedPath = resolver.ResolveImport(
            "@shared/api",
            "/repo/apps/web/src/components/App.tsx", TestContext.Current.CancellationToken);

        resolvedPath.Should().Be("/repo/src/shared/api.ts");
    }

    [Fact]
    public void WhenResolveImportUsesWorkspacePackageName_ThenItResolvesPackageSubpathsInsideTheRepository()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/apps/web/tsconfig.json",
                """
                {
                  "compilerOptions": {
                    "baseUrl": "."
                  }
                }
                """),
            ("/repo/apps/web/src/App.tsx", "export function App() { return null; }"),
            ("/repo/packages/ai-chat-api/package.json",
                """
                {
                  "name": "@loanmarket/ai-chat-api"
                }
                """),
            ("/repo/packages/ai-chat-api/tsconfig.json",
                """
                {
                  "compilerOptions": {
                    "baseUrl": "src"
                  }
                }
                """),
            ("/repo/packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts",
                "export function useGetConversationById() { return null; }"));
        var resolver = CreateResolver(fileSystem);

        var resolvedPath = resolver.ResolveImport(
            "@loanmarket/ai-chat-api/query-hooks/use-get-conversation-by-id-query.g",
            "/repo/apps/web/src/App.tsx", TestContext.Current.CancellationToken);

        resolvedPath.Should().Be("/repo/packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts");
    }

    [Fact]
    public void MatchingPathsPreferLongestPrefixBeforeWildcard()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/tsconfig.json", """
                { "compilerOptions": { "paths": { "@long/*": ["long/*"], "@*/suffix": ["short/*"] } } }
                """),
            ("/repo/app.ts", ""), ("/repo/long/name/suffix.ts", ""), ("/repo/short/long/name.ts", ""));

        CreateResolver(fileSystem).ResolveImport("@long/name/suffix", "/repo/app.ts", TestContext.Current.CancellationToken)
            .Should().Be("/repo/long/name/suffix.ts");
    }

    [Theory]
    [InlineData("@api", "@*")]
    [InlineData("@api/*", "@*")]
    public void MissingBestPathMappingDoesNotFallThroughToLessSpecificKey(string preferred, string fallback)
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/tsconfig.json", $$"""
                { "compilerOptions": { "paths": { "{{preferred}}": ["missing.ts"], "{{fallback}}": ["wrong.ts"] } } }
                """), ("/repo/app.ts", ""), ("/repo/wrong.ts", ""));

        CreateResolver(fileSystem).ResolveImport(preferred.Replace("*", "value", StringComparison.Ordinal), "/repo/app.ts", TestContext.Current.CancellationToken)
            .Should().BeNull();
    }

    [Fact]
    public void InheritedPathTargetsUseDerivedBaseUrl()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/base.json", """{ "compilerOptions": { "baseUrl": ".", "paths": { "@/*": ["src/*"] } } }"""),
            ("/repo/app/tsconfig.json", """{ "extends": "../base.json", "compilerOptions": { "baseUrl": "." } }"""),
            ("/repo/app/main.ts", ""), ("/repo/src/dep.ts", ""), ("/repo/app/src/dep.ts", ""));

        CreateResolver(fileSystem).ResolveImport("@/dep", "/repo/app/main.ts", TestContext.Current.CancellationToken)
            .Should().Be("/repo/app/src/dep.ts");
    }

    [Theory]
    [InlineData("./api", "/repo/api.d.ts")]
    [InlineData("./api.js", "/repo/api.d.ts")]
    [InlineData("./types", "/repo/types/index.d.ts")]
    public void DeclarationFilesResolveAsModules(string import, string expected)
    {
        var fileSystem = CreateRepositoryFileSystem(("/repo/tsconfig.json", "{}"), ("/repo/app.ts", ""),
            ("/repo/api.d.ts", "export declare function load(): void;"),
            ("/repo/types/index.d.ts", "export interface Options {}"));

        CreateResolver(fileSystem).ResolveImport(import, "/repo/app.ts", TestContext.Current.CancellationToken).Should().Be(expected);
    }

    [Theory]
    [InlineData(".ts")]
    [InlineData(".tsx")]
    public void SourceModulesRemainPreferredOverDeclarationFiles(string extension)
    {
        var fileSystem = CreateRepositoryFileSystem(("/repo/tsconfig.json", "{}"), ("/repo/app.ts", ""),
            ($"/repo/api{extension}", "export function load() {}"),
            ("/repo/api.d.ts", "export declare function load(): void;"));

        CreateResolver(fileSystem).ResolveImport("./api", "/repo/app.ts", TestContext.Current.CancellationToken).Should().Be($"/repo/api{extension}");
    }

    private static TsConfigResolver CreateResolver(MockFileSystem fileSystem)
        => new(
            new RepositoryWorkspace("/repo", "/test-storage/index.db", fileSystem),
            fileSystem);

    private static MockFileSystem CreateRepositoryFileSystem(params (string Path, string Content)[] files)
    {
        var fileData = new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        };

        foreach (var (path, content) in files)
        {
            fileData[path] = new MockFileData(content);
        }

        return new MockFileSystem(fileData, "/repo");
    }
}
