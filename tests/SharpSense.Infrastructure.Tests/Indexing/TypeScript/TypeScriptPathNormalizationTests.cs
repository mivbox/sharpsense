using System.IO.Abstractions;
using AwesomeAssertions;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TypeScriptPathNormalizationTests
{
    [Fact]
    public async Task PackageTraversalSkipsRepositorySymlinkCycle()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("sharpsense-ts-package-cycle-");
        try
        {
            var package = Path.Combine(directory.FullName, "packages", "shared");
            Directory.CreateDirectory(package);
            await File.WriteAllTextAsync(Path.Combine(package, "package.json"), """{"name":"@fixture/shared"}""", ct);
            await File.WriteAllTextAsync(Path.Combine(package, "index.ts"), "export const shared = 1;", ct);
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(package, "cycle"), directory.FullName);
            }
            catch (UnauthorizedAccessException)
            {
                Assert.Skip("Symbolic link creation requires permission on this platform.");
            }

            var fileSystem = new FileSystem();
            var workspace = new RepositoryWorkspace(directory.FullName, Path.Combine(directory.FullName, "index.db"), fileSystem);
            var resolver = new TsConfigResolver(workspace, fileSystem);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            var result = resolver.ResolveImport("@fixture/shared", Path.Combine(workspace.RootPath, "app.ts"), timeout.Token);

            Assert.Equal(Path.Combine(workspace.RootPath, "packages", "shared", "index.ts"), result);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SymlinkedRepositoryAndConfigTargetMatchPhysicalSourcePaths()
    {
        var ct = TestContext.Current.CancellationToken;
        var temporaryDirectory = Directory.CreateTempSubdirectory("sharpsense-ts-config-");
        try
        {
            var physicalRoot = Path.Combine(temporaryDirectory.FullName, "physical");
            var aliasRoot = Path.Combine(temporaryDirectory.FullName, "alias");
            Directory.CreateDirectory(Path.Combine(physicalRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(physicalRoot, "client", "src"));
            Directory.CreateDirectory(Path.Combine(physicalRoot, "shared"));
            await File.WriteAllTextAsync(Path.Combine(physicalRoot, "client", "tsconfig.json"), """
                { "files": ["../shared/api.ts"], "include": ["src"],
                  "compilerOptions": { "paths": { "@api": ["../shared/api.ts"] } } }
                """, ct);
            await File.WriteAllTextAsync(Path.Combine(physicalRoot, "client", "src", "app.ts"),
                "import { api } from '@api'; export function app() { return api(); }", ct);
            await File.WriteAllTextAsync(Path.Combine(physicalRoot, "shared", "api.ts"),
                "export function api() { return 1; }", ct);
            try
            {
                Directory.CreateSymbolicLink(aliasRoot, physicalRoot);
            }
            catch (UnauthorizedAccessException)
            {
                Assert.Skip("Symbolic link creation requires permission on this platform.");
            }

            var fileSystem = new FileSystem();
            var workspace = new RepositoryWorkspace(aliasRoot, Path.Combine(temporaryDirectory.FullName, "index.db"), fileSystem);
            var resolver = new TsConfigResolver(workspace, fileSystem);
            var discoverer = new TypeScriptSourceDiscoverer(workspace,
                new WorkspaceFileDiscoverer(workspace, fileSystem), fileSystem, resolver);
            var extractor = new TypeScriptLanguageExtractor(discoverer, fileSystem,
                [new CodeNodeExtractionPass(), new ImportDependencyPass(resolver, workspace)]);

            var result = await extractor.Extract(new(Path.Combine(aliasRoot, "client", "tsconfig.json"), null),
                ct);

            result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));
            result.Value.CodeNodes.Should().HaveCount(2);
            var edge = result.Value.Edges.Should().ContainSingle().Which;
            edge.CallerId.Should().Be("code:ts:client/src/app.ts:app");
            edge.CalleeId.Should().Be("code:ts:shared/api.ts:api");
            edge.EdgeType.Should().Be(EdgeType.Import);
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }
}
