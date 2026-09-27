using AwesomeAssertions;
using FluentResults;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TypeScriptRestorationTests
{
    [Fact]
    public async Task WhenDuplicateNames_ThenRemainDistinctAndBodyChangesInvalidateHash()
    {
        var (fs, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("a.ts", "/** Loads customer data. */\nexport function load() { return 1; }"),
            ("b.ts", "export function load() { return 2; }"));
        var before = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));
        before.CodeNodes.Select(node => node.FullyQualifiedName).Should().BeEquivalentTo("ts:a.ts::load", "ts:b.ts::load");
        before.CodeNodes.Single(node => node.RelativeFilePath == "a.ts").Summary.Should().Be("Loads customer data.");
        before.CodeNodes.Should().OnlyContain(node => node.BodyHash != null && node.BodyHash.Length == 64);
        fs.File.WriteAllText("/repo/a.ts", "/** Loads customer data. */\nexport function load() { return 3; }");
        var after = Value(await extractor.Extract(
            new ExtractionContext(
                "/repo/tsconfig.json",
                Progress: null,
                ChangedFiles: [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/a.ts")]),
            _ct));
        after.CodeNodes.Single(node => node.RelativeFilePath == "a.ts").BodyHash.Should().NotBe(before.CodeNodes.Single(node => node.RelativeFilePath == "a.ts").BodyHash);
        after.CodeNodes.Single(node => node.RelativeFilePath == "b.ts").BodyHash.Should().Be(before.CodeNodes.Single(node => node.RelativeFilePath == "b.ts").BodyHash);
    }

    [Fact]
    public async Task WhenAliasesDefaultsAndBarrels_ThenResolveToOriginalDeclarations()
    {
        var (_, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("leaf.ts", "function original() { return 1; } export { original as renamed }; export default () => 2;"),
            ("barrel.ts", "export { renamed as publicName, default } from './leaf.js';"),
            ("app.ts", "import callDefault, { publicName } from './barrel.js'; export function app() { return publicName() + callDefault(); }"));
        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));
        result.Edges.Where(edge => edge.CallerId == "code:ts:app.ts:app" && edge.EdgeType == EdgeType.Import)
            .Select(edge => edge.CalleeId).Should().BeEquivalentTo("code:ts:leaf.ts:original", "code:ts:leaf.ts:default");
        result.CodeNodes.Should().Contain(node => node.CanonicalId == "code:ts:leaf.ts:default" && node.NodeType == NodeType.Method);
    }

    [Theory]
    [InlineData("import { original as local } from './leaf'; export { local as renamed }; export default local;", "original")]
    [InlineData("import local from './leaf'; export { local as renamed }; export default local;", "default")]
    public async Task WhenBindingsAreReexported_ThenNamedAndDefaultBarrelsResolve(string barrel, string declaration)
    {
        var (_, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("leaf.ts", "export function original() { return 1; } export default () => 2;"),
            ("barrel.ts", barrel),
            ("forward.ts", "export { renamed, default } from './barrel';"),
            ("named.ts", "import { renamed } from './forward'; export function named() { return renamed(); }"),
            ("default.ts", "import run from './forward'; export function runDefault() { return run(); }"));

        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));

        result.Edges.Where(edge => edge.EdgeType == EdgeType.Import).Should().HaveCount(2).And.OnlyContain(edge => edge.CalleeId == $"code:ts:leaf.ts:{declaration}");
    }

    [Fact]
    public async Task WhenCommonDeclarationsAndMembers_ThenHaveDistinctNodesWithoutNestedLocalExports()
    {
        var (_, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("api.ts", """
            export interface Options { name: string; run(): void; }
            export type State = 'ready' | 'waiting';
            export enum Mode { Read, Write }
            export class Client { request() { return fetch('/api'); } }
            export const load = () => { const hidden = () => 1; return hidden(); };
            """));
        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));
        result.CodeNodes.Should().Contain(node => node.FullyQualifiedName == "ts:api.ts::Options" && node.NodeType == NodeType.Interface);
        result.CodeNodes.Should().Contain(node => node.FullyQualifiedName == "ts:api.ts::State");
        result.CodeNodes.Should().Contain(node => node.FullyQualifiedName == "ts:api.ts::Mode");
        result.CodeNodes.Should().Contain(node => node.FullyQualifiedName == "ts:api.ts::Client.request");
        result.CodeNodes.Should().NotContain(node => node.DisplayName.Contains("hidden", StringComparison.Ordinal));
        result.Edges.Should().Contain(edge => edge.CallerId == "code:ts:api.ts:Client.request" && edge.EdgeType == EdgeType.HttpRequest);
    }

    [Fact]
    public async Task WhenConfigsInheritAndReferenceProjects_ThenRootsAreFilteredAndScopeIsExtended()
    {
        var (_, extractor) = Fixture(
            ("base.json", """{ "include": ["src/**/*.ts"], "exclude": ["src/ignored.ts"] }"""),
            ("tsconfig.json", """{ "extends": "./base.json", "references": [{ "path": "./shared" }] }"""),
            ("src/keep.ts", "export function keep() {}"),
            ("src/ignored.ts", "export function ignored() {}"),
            ("outside.ts", "export function outside() {}"),
            ("shared/tsconfig.json", """{ "files": ["api.ts"] }"""),
            ("shared/api.ts", "export function shared() {}"),
            ("shared/ignored.ts", "export function other() {}"));
        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));
        result.CodeNodes.Select(node => node.RelativeFilePath).Should().BeEquivalentTo("src/keep.ts", "shared/api.ts");
    }

    [Theory]
    [InlineData("/repo/client/tsconfig.app.json")]
    [InlineData("/repo/client/tsconfig.json")]
    public async Task WhenConfigsAreExplicitOrReferenced_ThenEachOwnsItsImportResolution(string target)
    {
        var (_, extractor) = Fixture(
            ("client/tsconfig.json", """{ "files": [], "references": [{ "path": "./tsconfig.app.json" }] }"""),
            ("client/tsconfig.app.json", """
                { "include": ["src"], "compilerOptions": { "paths": { "@shared/*": ["../shared/*"] } } }
                """),
            ("client/src/app.ts", "import { wrapper } from '../../shared/wrapper.js'; export function app() { return wrapper(); }"),
            ("shared/wrapper.ts", "import { api } from '@shared/api'; export function wrapper() { return api(); }"),
            ("shared/api.ts", "export function api() { return 1; }"));

        var result = Value(await extractor.Extract(new(target, null), _ct));

        result.Edges.Should().Contain(edge => edge.CallerId == "code:ts:shared/wrapper.ts:wrapper" &&
            edge.CalleeId == "code:ts:shared/api.ts:api" && edge.EdgeType == EdgeType.Import);
        result.Edges.Should().NotContain(edge => edge.CalleeId.StartsWith("package:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{ \"files\": [\"../shared/api.ts\"] }")]
    [InlineData("{ \"include\": [\"../shared/**/*.ts\"] }")]
    [InlineData("{ \"extends\": \"../base.json\" }")]
    public async Task WhenConfiguredRootsOutsideConfigDirectory_ThenRemainWithinRepository(string config)
    {
        var (_, extractor) = Fixture(
            ("base.json", """{ "include": ["shared/**/*.ts", "../outside/**/*.ts"] }"""),
            ("client/tsconfig.json", config),
            ("client/unrelated.ts", "export function unrelated() {}"),
            ("shared/api.ts", "export function api() {}"),
            ("shared/node_modules/ignored.ts", "export function ignored() {}"),
            ("../outside/api.ts", "export function outside() {}"));

        var result = Value(await extractor.Extract(new("/repo/client/tsconfig.json", null), _ct));

        result.CodeNodes.Should().ContainSingle().Which.RelativeFilePath.Should().Be("shared/api.ts");
    }

    [Fact]
    public async Task WhenReferencedProjects_ThenKeepTheirOwnAliasesWhenImportedByParent()
    {
        var (_, extractor) = Fixture(
            ("tsconfig.json", """
                { "include": ["app.ts"], "references": [{ "path": "./shared/tsconfig.app.json" }],
                  "compilerOptions": { "paths": { "@api": ["wrong.ts"] } } }
                """),
            ("app.ts", "import { wrapper } from './shared/wrapper.js'; export function app() { return wrapper(); }"),
            ("wrong.ts", "export function api() {}"),
            ("shared/tsconfig.app.json", """
                { "include": ["wrapper.ts"], "compilerOptions": { "paths": { "@api": ["api.ts"] } } }
                """),
            ("shared/wrapper.ts", "import { api } from '@api'; export function wrapper() { return api(); }"),
            ("shared/api.ts", "export function api() {}"));

        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));

        result.Edges.Should().Contain(edge => edge.CallerId == "code:ts:shared/wrapper.ts:wrapper" &&
            edge.CalleeId == "code:ts:shared/api.ts:api");
        result.CodeNodes.Should().NotContain(node => node.RelativeFilePath == "wrong.ts");
    }

    [Fact]
    public async Task WhenSiblingModulesChange_ThenConsumersRebuildAndDeletedExportsDisappear()
    {
        var (fs, extractor) = Fixture(
            ("app/tsconfig.json", """{ "include": ["src"] }"""),
            ("app/src/app.ts", "import { shared } from '../../shared/api.js'; export function app() { return shared(); }"),
            ("shared/api.ts", "export function shared() { return 1; }"));
        var initial = Value(await extractor.Extract(new("/repo/app/tsconfig.json", null), _ct));
        initial.CodeNodes.Should().HaveCount(2);
        fs.File.WriteAllText("/repo/shared/api.ts", "export function shared() { return 2; }");
        var changed = Value(await extractor.Extract(
            new ExtractionContext(
                "/repo/app/tsconfig.json",
                Progress: null,
                ChangedFiles: [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/shared/api.ts")]),
            _ct));
        changed.CodeNodes.Should().HaveCount(2);
        changed.Edges.Should().Contain(edge => edge.CallerId == "code:ts:app/src/app.ts:app" && edge.CalleeId == "code:ts:shared/api.ts:shared");
        fs.File.Delete("/repo/shared/api.ts");
        var deleted = Value(await extractor.Extract(
            new ExtractionContext(
                "/repo/app/tsconfig.json",
                Progress: null,
                ChangedFiles: [new(WorkspaceFileChangeAction.Deleted, OldPath: "/repo/shared/api.ts")]),
            _ct));
        deleted.CodeNodes.Should().ContainSingle();
        deleted.Edges.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenMalformedSource_ThenReturnsFailureInsteadOfPartialReplacement()
    {
        var (fs, extractor) = Fixture(("tsconfig.json", "{}"), ("app.ts", "export function app() { return 1; }"));
        Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct)).CodeNodes.Should().ContainSingle();
        fs.File.WriteAllText("/repo/app.ts", "export function app( {");
        var failed = await extractor.Extract(
            new ExtractionContext(
                "/repo/tsconfig.json",
                Progress: null,
                ChangedFiles: [new(WorkspaceFileChangeAction.Modified, NewPath: "/repo/app.ts")]),
            _ct);
        failed.IsFailed.Should().BeTrue();
        failed.Errors.Should().Contain(error => error.Message.Contains("syntax errors", StringComparison.Ordinal));
        failed.Errors.Should().Contain(error => error.Metadata.ContainsKey("filePath") &&
            Equals(error.Metadata["filePath"], "app.ts"));
    }

    [Fact]
    public async Task WhenHttpCallsShareALine_ThenEachBelongsToItsContainingFunction()
    {
        var (_, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("app.ts", "export function first() { return 1; } export function second() { return fetch('/api'); }"));
        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));
        result.Edges.Single(edge => edge.EdgeType == EdgeType.HttpRequest).CallerId.Should().Be("code:ts:app.ts:second");
    }

    [Fact]
    public async Task WhenOverloadsHaveImplementations_ThenImplementationsOwnHashesAndHttpCalls()
    {
        var (fs, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("api.ts", """
            export function load(id: string): unknown;
            export function load(id: number): unknown;
            export function load(id: string | number) { return fetch('/load'); }
            export class Client {
                get(id: string): unknown;
                get(id: number): unknown;
                get(id: string | number) { return fetch('/get'); }
            }
            """));
        var before = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));
        before.Edges.Should().Contain(edge => edge.CallerId == "code:ts:api.ts:load" && edge.EdgeType == EdgeType.HttpRequest);
        before.Edges.Should().Contain(edge => edge.CallerId == "code:ts:api.ts:Client.get" && edge.EdgeType == EdgeType.HttpRequest);
        fs.File.WriteAllText(
            "/repo/api.ts",
            fs.File.ReadAllText("/repo/api.ts")
                .Replace("'/load'", "'/new-load'")
                .Replace("'/get'", "'/new-get'"));
        var after = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));
        foreach (var name in new[]
        {
            "load",
            "Client.get"
        })
        {
            after.CodeNodes.Single(node => node.CanonicalId == $"code:ts:api.ts:{name}").BodyHash
                .Should().NotBe(before.CodeNodes.Single(node => node.CanonicalId == $"code:ts:api.ts:{name}").BodyHash);
        }
    }

    [Fact]
    public async Task WhenImportLikeTextInComments_ThenDoesNotExpandDiscovery()
    {
        var (_, extractor) = Fixture(
            ("app/tsconfig.json", "{}"),
            ("app/app.ts", "/*\nimport { hidden } from '../hidden';\n*/\nexport function app() {}"),
            ("hidden.ts", "export function hidden() {}"));
        var result = Value(await extractor.Extract(new("/repo/app/tsconfig.json", null), _ct));
        result.CodeNodes.Should().ContainSingle();
    }

    [Theory]
    [InlineData("run:static", "static")]
    [InlineData("run", "instance")]
    [InlineData("value:get", "get")]
    [InlineData("value:set", "set")]
    [InlineData("value:static:get", "static-get")]
    [InlineData("value:static:set", "static-set")]
    public async Task WhenClassMembersAreDistinct_ThenEachOwnsItsHashesAndHttpCalls(string member, string url)
    {
        const string source = """
            export class Client {
                static run() { return fetch('/static'); }
                run() { return fetch('/instance'); }
                get value() { fetch('/get'); return 1; }
                set value(input: number) { fetch('/set'); }
                static get value() { fetch('/static-get'); return 1; }
                static set value(input: number) { fetch('/static-set'); }
            }
            """;
        var (fs, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("api.ts", source),
            ("app.ts", "import { Client } from './api'; export function app() { return new Client(); }"));
        var before = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));
        var memberId = $"code:ts:api.ts:Client.{member}";
        before.CodeNodes.Should().HaveCount(8);
        before.CodeNodes.Select(node => node.FullyQualifiedName)
            .Distinct().Should().HaveCount(8);
        before.Edges.Where(edge => edge.EdgeType == EdgeType.HttpRequest).Should().HaveCount(6).And.OnlyContain(edge => edge.CallerId != "code:ts:api.ts:Client");
        before.Edges.Should().Contain(edge => edge.CallerId == memberId && edge.Metadata == $"/{url}" &&
            edge.EdgeType == EdgeType.HttpRequest);
        before.Edges.Where(edge => edge.EdgeType == EdgeType.Import).Should().ContainSingle().Which.CalleeId.Should().Be("code:ts:api.ts:Client");

        fs.File.WriteAllText(
            "/repo/api.ts",
            source.Replace(
                $"'/{url}'",
                "'/changed'",
                StringComparison.Ordinal));
        var after = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));

        after.CodeNodes.Where(node => node.BodyHash != before.CodeNodes.Single(old => old.CanonicalId == node.CanonicalId).BodyHash)
            .Select(node => node.CanonicalId).Should().BeEquivalentTo("code:ts:api.ts:Client", memberId);
        after.Edges.Should().Contain(edge => edge.CallerId == memberId && edge.Metadata == "/changed" &&
            edge.EdgeType == EdgeType.HttpRequest);
    }

    [Theory]
    [InlineData("import { foo /* comment */ } from './leaf';")]
    [InlineData("import { foo as\n local } from './leaf';")]
    [InlineData("import { foo as\tlocal } from './leaf';")]
    [InlineData("import { foo } from /* comment */ './leaf';")]
    [InlineData("import/* comment */ { foo } from './leaf';")]
    [InlineData("import { type foo as local } from './leaf';")]
    [InlineData("import { \"foo\" as local } from './leaf';")]
    [InlineData("import /* comment */ local from './leaf';")]
    public async Task WhenImportSyntaxNodes_ThenPreserveBindingsAcrossCommentsAndWhitespace(string import)
    {
        var (_, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("leaf.ts", "export function foo() {} export default foo; export function unused() {}"),
            ("app.ts", import + " export function app() {}"));

        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));

        result.Edges.Where(edge => edge.EdgeType == EdgeType.Import).Should().ContainSingle().Which.CalleeId.Should().Be("code:ts:leaf.ts:foo");
    }

    [Theory]
    [InlineData("import { foo } from './barrel';", "export * from './a'; export { foo } from './b';")]
    [InlineData("import { foo } from './barrel';", "export { foo } from './b'; export * from './a';")]
    [InlineData("import * as api from './barrel';", "export * from './a'; export { foo } from './b';")]
    [InlineData("import * as api from './barrel';", "export { foo } from './b'; export * from './a';")]
    public async Task WhenExplicitReexportsOverlapStars_ThenExplicitExportsWinForNamedAndNamespaceImports(string import, string barrel)
    {
        var (_, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            ("a.ts", "export function foo() {} export default () => 1;"),
            ("b.ts", "export function foo() {}"),
            ("barrel.ts", barrel),
            ("app.ts", import + " export function app() {}"));

        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));

        result.Edges.Where(edge => edge.EdgeType == EdgeType.Import).Should().ContainSingle().Which.CalleeId.Should().Be("code:ts:b.ts:foo");
    }

    [Theory]
    [InlineData("api.ts")]
    [InlineData("api.d.ts")]
    public async Task WhenExportedAmbientDeclarationsAndTheirMembers_ThenRemainImportable(string path)
    {
        var (_, extractor) = Fixture(
            ("tsconfig.json", "{}"),
            (path, "export declare function load(): void; export declare class Client { run(): void; } export declare const version: string;"),
            ("app.ts", "import { load, Client, version } from './api'; export function app() {}"));

        var result = Value(await extractor.Extract(new("/repo/tsconfig.json", null), _ct));

        result.CodeNodes.Select(node => node.CanonicalId).Should().BeEquivalentTo(
            $"code:ts:{path}:load",
            $"code:ts:{path}:Client",
            $"code:ts:{path}:Client.run",
            $"code:ts:{path}:version",
            "code:ts:app.ts:app");
        result.Edges.Where(edge => edge.EdgeType == EdgeType.Import)
            .Select(edge => edge.CalleeId)
            .Should().BeEquivalentTo(
            $"code:ts:{path}:load",
            $"code:ts:{path}:Client",
            $"code:ts:{path}:version");
    }

    private static CancellationToken _ct => TestContext.Current.CancellationToken;

    private static ExtractedNodes Value(Result<ExtractedNodes> result)
    {
        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));

        return result.Value;
    }

    private static (MockFileSystem Files, TypeScriptLanguageExtractor Extractor) Fixture(params (string Path, string Content)[] files)
    {
        var data = new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        };
        foreach (var file in files)
        {
            data["/repo/" + file.Path] = new(file.Content);
        }

        var fs = new MockFileSystem(data, "/repo");
        var workspace = new RepositoryWorkspace("/repo", "/test-storage/index.db", fs);
        var resolver = new TsConfigResolver(workspace, fs);
        var discoverer = new TypeScriptSourceDiscoverer(workspace, new WorkspaceFileDiscoverer(workspace, fs), fs, resolver);

        return (fs, new(
            discoverer,
            fs,
            [new CodeNodeExtractionPass(), new ImportDependencyPass(resolver, workspace), new HttpEdgeExtractionPass()]));
    }
}
