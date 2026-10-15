using AwesomeAssertions;
using SharpSense.TypeScript.Parsing;
using System.Text.Json;

namespace SharpSense.TypeScript.Tests.Compatibility;

public sealed class CompilerCorpusTests
{
    private static readonly Corpus _corpus = JsonSerializer.Deserialize<Corpus>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Compatibility", "corpus.json")),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static IEnumerable<object[]> Cases => _corpus.Cases.Select(test => new object[] { test.Id });

    [Theory]
    [MemberData(nameof(Cases))]
    public void WhenParsingCompilerFixture_ThenMatchesOfficialSyntaxDiagnosticsAndStatementRanges(string id)
    {
        var expected = _corpus.Cases.Single(test => test.Id == id);

        var tree = TypeScriptParser.Parse(expected.FileName, expected.SourceText, TestContext.Current.CancellationToken);

        tree.Diagnostics
            .Select(diagnostic => new Diagnostic(diagnostic.Code, diagnostic.Start, diagnostic.Length))
            .Should()
            .Equal(expected.Diagnostics, $"the fixture is pinned to TypeScript {_corpus.CompilerVersion}: {id}");
        tree.Root.Statements!
            .Select(statement => new Statement(statement.Kind.ToString(), tree.GetStart(statement), statement.End!.Value))
            .Should()
            .Equal(
                expected.Statements,
                $"top-level syntax and source ranges must match: {id}");
    }

    private sealed record Corpus(string CompilerVersion, IReadOnlyList<CompilerCase> Cases);

    private sealed record CompilerCase(
        string Id,
        string FileName,
        string SourceText,
        IReadOnlyList<Diagnostic> Diagnostics,
        IReadOnlyList<Statement> Statements);

    private sealed record Diagnostic(int Code, int Start, int Length);

    private sealed record Statement(string Kind, int Start, int End);
}
