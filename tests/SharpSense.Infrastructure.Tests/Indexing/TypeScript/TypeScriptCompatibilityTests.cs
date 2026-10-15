using AwesomeAssertions;
using Moq;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using static SharpSense.Infrastructure.Tests.Indexing.TypeScript.TypeScriptTestPaths;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TypeScriptCompatibilityTests
{
    [Theory]
    [InlineData("ts", "export type Subject<out T> = () => T;")]
    [InlineData("tsx", "export type Subject<in out T> = (value: T) => T;")]
    [InlineData("ts", "export interface Subject { check: (string) => boolean; }")]
    [InlineData("tsx", "export interface Subject { check: (number) => boolean; }")]
    [InlineData("ts", "jest.mock<typeof import('library')>('library', () => ({}),); export const Subject = 1;")]
    [InlineData("tsx", "jest.mock<typeof import('library')>('library', () => ({}),); export const Subject = 1;")]
    [InlineData("tsx", "export const Subject = () => <div>A & B &amp; C &#38; D &#x26; E &</div>;")]
    public async Task WhenCompatibleSyntax_ThenExtractsTheDeclaration(string extension, string source)
    {
        var (_, extractor) = Create(("subject." + extension, source));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue(string.Join("; ", result.Errors.Select(error => error.Message)));
        result.Value.CodeNodes.Should().Contain(node => node.CanonicalId == $"code:ts:subject.{extension}:Subject");
        result.Value.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData("export type * from '../shared/model';", "import type { Model } from './barrel';")]
    [InlineData("export type * as models from '../shared/model';", "import type { models } from './barrel';")]
    public async Task WhenTypeOnlyBarrel_ThenDiscoversAndResolvesTheOriginalDeclaration(string barrel, string import)
    {
        var (_, extractor) = Create(
            ("tsconfig.json", """{ "files": ["src/app.ts"] }"""),
            ("src/app.ts", import + " export function consume() {}"),
            ("src/barrel.ts", barrel),
            ("shared/model.ts", "export interface Model { name: string; }"));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.InputPaths.Should().Contain(Absolute("/repo/shared/model.ts"));
        result.Value.Edges.Should().Contain(edge => edge.EdgeType == EdgeType.Import &&
            edge.CallerId == "code:ts:src/app.ts:consume" && edge.CalleeId == "code:ts:shared/model.ts:Model");
    }

    [Theory]
    [InlineData("ts", "")]
    [InlineData("ts", ",")]
    [InlineData("tsx", "")]
    [InlineData("tsx", ",")]
    public async Task WhenImportTypeArgumentInHttpCall_ThenExtractsTheRequest(string extension, string trailingComma)
    {
        var (_, extractor) = Create(
            ("app." + extension, "export function load() { return axios.get<typeof import('library')>('/orders', {}" + trailingComma + "); }"));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Edges.Should().Contain(edge => edge.EdgeType == EdgeType.HttpRequest &&
            edge.CallerId == $"code:ts:app.{extension}:load" && edge.CalleeId == "http:GET:%2Forders");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task WhenTypeAndValueShareAName_ThenIndexesTheValueRegardlessOfOrder(bool typeFirst, bool exportDefault)
    {
        const string type = "export type Status = typeof Status[keyof typeof Status];";
        const string value = "Status = { Active: 1 } as const";
        var source = typeFirst ? type + "\nexport const " + value : "export const " + value + "\n" + type;
        if (exportDefault)
        {
            source += "\nexport default Status;";
        }

        var (_, extractor) = Create(("status.ts", source));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var node = result.Value.CodeNodes.Should().ContainSingle().Subject;
        node.CanonicalId.Should().Be("code:ts:status.ts:Status");
        node.NodeType.Should().Be(NodeType.Field);
        node.StartLine.Should().Be(typeFirst ? 2 : 1);
        node.BodyHash.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant());
    }

    [Fact]
    public async Task WhenClassHasPrivateAndComputedMembers_ThenIndexesOnlyNamedPublicMembers()
    {
        var (_, extractor) = Create(("model.ts", "export class Model { #secret = 1; [field] = 2; public value = 3; run() {} }"));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.CodeNodes
            .Select(node => node.CanonicalId)
            .Should()
            .BeEquivalentTo(
                "code:ts:model.ts:Model",
                "code:ts:model.ts:Model.value",
                "code:ts:model.ts:Model.run");
    }

    [Fact]
    public async Task WhenExportModifiersPrecedeDeclarationOnAnotherLine_ThenPreservesDeclarationLocationAndHash()
    {
        const string declaration = "async function load() { return fetch('/items'); }";
        var (_, extractor) = Create(("api.ts", "/** Loads items. */\nexport default /* export note */\n" + declaration));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var node = result.Value.CodeNodes.Should().ContainSingle().Subject;
        node.CanonicalId.Should().Be("code:ts:api.ts:load");
        node.StartLine.Should().Be(3);
        node.Summary.Should().Be("Loads items.");
        node.BodyHash.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(declaration))).ToLowerInvariant());
    }

    [Fact]
    public async Task WhenJsxContainsUnicodeAndAmpersands_ThenPreservesLocationsAndBodyHash()
    {
        const string declaration = "View = () => <div>😀 A & B</div>";
        var (_, extractor) = Create(("view.tsx", "// café\nexport const " + declaration + ";\n"));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var node = result.Value.CodeNodes.Should().ContainSingle().Subject;
        node.NodeType.Should().Be(NodeType.Component);
        node.StartLine.Should().Be(2);
        node.BodyHash.Should().Be(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(declaration))).ToLowerInvariant());
    }

    [Theory]
    [InlineData("app.ts", "export function broken( {", 1005)]
    [InlineData("app.tsx", "export const View = () => <div>A & B;", 17008)]
    [InlineData("app.ts", "export type * from ;", 1109)]
    [InlineData("app.ts", "export type Bad<out in T> = T;", 1029)]
    public async Task WhenMalformedSource_ThenFailsWithLocation(string path, string source, int diagnosticCode)
    {
        var (_, extractor) = Create((path, source));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.Single(error => error.Metadata.ContainsKey("filePath"));
        error.Metadata["filePath"].Should().Be(path);
        error.Metadata["line"].Should().Be(1);
        error.Metadata["diagnosticCode"].Should().Be(diagnosticCode);
        error.Message.Should().Contain("invalid or unsupported syntax");
    }

    [Fact]
    public async Task WhenMissingTokenAfterUnicode_ThenReportsEditorColumn()
    {
        const string source = "export function run() { const text = '😀é';";
        var (_, extractor) = Create(("app.ts", source));

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.Single(error => error.Metadata.ContainsKey("filePath"));
        error.Metadata["column"].Should().Be(source.Length + 1);
        error.Message.Should().Contain("TS1005: '}' expected.");
    }

    [Fact]
    public async Task WhenManyFilesFail_ThenBoundsDiagnosticsAndSkipsExtractionPasses()
    {
        var files = Enumerable.Range(0, 55)
            .Select(index => ($"broken{index:D2}.ts", "export function broken( {"))
            .Append(("valid.ts", "export const valid = 1;"))
            .ToArray();
        var (fs, _) = Create(files);
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        var extractor = CreateExtractor(fs, [pass.Object]);

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().HaveCount(50);
        result.Errors[0].Message.Should().Contain("55 file(s)").And.Contain("6 omitted");
        result.Errors
            .Skip(1)
            .Select(error => error.Metadata["filePath"])
            .Should()
            .Equal(Enumerable.Range(0, 49).Select(index => $"broken{index:D2}.ts"));
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);
    }

    [Fact]
    public async Task WhenSourceChangesAfterDiscovery_ThenCurrentAnalysisUsesItsSnapshotAndTheNextReadsTheChange()
    {
        var (fs, extractor) = Create(
            ("a.ts", "export const first = 1;"),
            ("b.ts", "export const second = 2;"));
        var progress = new SourceChangingProgress(fs);

        var snapshot = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), progress), TestContext.Current.CancellationToken);
        var changed = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);
        fs.File.WriteAllText(Absolute("/repo/b.ts"), "export const replacement = 3;");
        var recovered = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        snapshot.IsSuccess.Should().BeTrue();
        snapshot.Value.CodeNodes
            .Select(node => node.CanonicalId)
            .Should()
            .BeEquivalentTo(
                "code:ts:a.ts:first",
                "code:ts:b.ts:second");
        changed.IsFailed.Should().BeTrue();
        changed.Errors.Should().Contain(error => error.Metadata.ContainsKey("filePath") && (string)error.Metadata["filePath"] == "b.ts");
        recovered.IsSuccess.Should().BeTrue();
        recovered.Value.CodeNodes
            .Select(node => node.CanonicalId)
            .Should()
            .BeEquivalentTo(
                "code:ts:a.ts:first",
                "code:ts:b.ts:replacement");
    }

    [Fact]
    public async Task WhenSourceExceedsParserLimits_ThenReportsTheFileAndSkipsExtraction()
    {
        var (fs, _) = Create(("deep.ts", "export const value = " + new string('(', 1_000) + "1" + new string(')', 1_000) + ";"));
        var pass = new Mock<ITypeScriptExtractionPass>(MockBehavior.Strict);
        var extractor = CreateExtractor(fs, [pass.Object]);

        var result = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);

        result.IsFailed.Should().BeTrue();
        var error = result.Errors.Should().ContainSingle().Subject;
        error.Metadata["filePath"].Should().Be("deep.ts");
        error.Message.Should().Contain("MaxRecursionDepth");
        pass.Verify(candidate => candidate.Execute(It.IsAny<TypeScriptPassContext>()), Times.Never);
    }

    [Fact]
    public async Task WhenParsingCancelled_ThenCanExtractAgain()
    {
        var (_, extractor) = Create(("app.ts", "export const value = 1;"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new CancellingProgress(cancellation);

        var action = () => extractor.Extract(new(Absolute("/repo/tsconfig.json"), progress), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        var recovered = await extractor.Extract(new(Absolute("/repo/tsconfig.json"), null), TestContext.Current.CancellationToken);
        recovered.IsSuccess.Should().BeTrue();
        recovered.Value.CodeNodes.Should().ContainSingle();
    }

    private static (MockFileSystem Files, TypeScriptLanguageExtractor Extractor) Create(params (string Path, string Text)[] files)
    {
        var fs = new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                [Absolute("/repo/.git/HEAD")] = new("ref: refs/heads/main"),
                [Absolute("/repo/tsconfig.json")] = new("{}")
            },
            Absolute("/repo"));
        foreach (var (path, text) in files)
        {
            fs.AddFile(Path.Combine(Absolute("/repo"), path), new MockFileData(text));
        }

        return (fs, CreateExtractor(fs));
    }

    private static TypeScriptLanguageExtractor CreateExtractor(
        MockFileSystem fs,
        ITypeScriptExtractionPass[]? passes = null)
    {
        var workspace = new RepositoryWorkspace(Absolute("/repo"), Absolute("/test-storage/index.db"), fs);
        var resolver = new TsConfigResolver(workspace, fs);
        var discoverer = new TypeScriptSourceDiscoverer(workspace, new WorkspaceFileDiscoverer(workspace, fs), fs, resolver);

        return new(
            discoverer,
            fs,
            passes ?? [new CodeNodeExtractionPass(), new ImportDependencyPass(resolver, workspace), new HttpEdgeExtractionPass()]);
    }

    private sealed class CancellingProgress(CancellationTokenSource cancellation) : IProgress<IndexingProgress>
    {
        public void Report(IndexingProgress value)
        {
            if (value.CurrentTask.StartsWith("Parsing ", StringComparison.Ordinal))
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class SourceChangingProgress(MockFileSystem files) : IProgress<IndexingProgress>
    {
        public void Report(IndexingProgress value)
        {
            if (value.CurrentTask == "Parsing a.ts...")
            {
                files.File.WriteAllText(Absolute("/repo/b.ts"), "export const broken = ;");
            }
        }
    }
}
