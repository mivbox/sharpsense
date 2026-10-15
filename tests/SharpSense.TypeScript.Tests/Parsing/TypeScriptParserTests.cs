using AwesomeAssertions;
using SharpSense.TypeScript.Parsing;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.TypeScript.Tests.Parsing;

public sealed class TypeScriptParserTests
{
    [Theory]
    [InlineData("export type * from './model';", SyntaxKind.ExportDeclaration)]
    [InlineData("export type * as models from './model';", SyntaxKind.ExportDeclaration)]
    [InlineData("export type Producer<out T> = () => T;", SyntaxKind.TypeAliasDeclaration)]
    [InlineData("export interface Check { run: (string) => boolean; }", SyntaxKind.InterfaceDeclaration)]
    [InlineData("mock<typeof import('library')>('library', () => ({}),);", SyntaxKind.ExpressionStatement)]
    [InlineData("export const options = { count: 1 } satisfies Options;", SyntaxKind.VariableStatement)]
    [InlineData("export function build<const T>(value: T) { return value; }", SyntaxKind.FunctionDeclaration)]
    [InlineData("import data from './data.json' with { type: 'json' };", SyntaxKind.ImportDeclaration)]
    [InlineData("using resource = acquire();", SyntaxKind.VariableStatement)]
    [InlineData("export class Example { @logged accessor value = 1; }", SyntaxKind.ClassDeclaration)]
    public void WhenSupportedSyntax_ThenProducesTheExpectedStatement(string source, SyntaxKind expectedKind)
    {
        var tree = TypeScriptParser.Parse("source.ts", source, TestContext.Current.CancellationToken);

        tree.Diagnostics.Should().BeEmpty();
        tree.Root.Statements.Should().ContainSingle().Which.Kind.Should().Be(expectedKind);
        tree.SourceText.Should().Be(source);
    }

    [Theory]
    [InlineData("component.tsx")]
    [InlineData("component.TSX")]
    public void WhenJsxContainsUnicodeAndAmpersands_ThenRetainsTheElementAndText(string fileName)
    {
        const string source = "export const View = () => <div>😀 A & B &amp; C</div>;";

        var tree = TypeScriptParser.Parse(fileName, source, TestContext.Current.CancellationToken);

        tree.Diagnostics.Should().BeEmpty();
        var element = tree.Root
            .DescendantsAndSelf(TestContext.Current.CancellationToken)
            .OfType<JsxElement>()
            .Should()
            .ContainSingle().Subject;
        tree.GetText(element).Should().Be("<div>😀 A & B &amp; C</div>");
        element.Parent.Should().BeOfType<ArrowFunction>();
    }

    [Fact]
    public void WhenStringUsesExtendedUnicodeEscape_ThenDecodesTheCodePoint()
    {
        var tree = TypeScriptParser.Parse("unicode.ts", "const value = '\\u{1f600}';", TestContext.Current.CancellationToken);

        var literal = tree.Root
            .DescendantsAndSelf(TestContext.Current.CancellationToken)
            .OfType<StringLiteral>()
            .Should()
            .ContainSingle().Subject;

        tree.Diagnostics.Should().BeEmpty();
        literal.Text.Should().Be("😀");
    }

    [Fact]
    public void WhenErrorsOccurAtDifferentLocations_ThenRetainsEachDiagnosticOnce()
    {
        const string source = "const first = ;\nconst second = ;";

        var tree = TypeScriptParser.Parse("broken.ts", source, TestContext.Current.CancellationToken);

        tree.Diagnostics
            .Select(diagnostic => diagnostic.Start)
            .Should()
            .Equal(
                source.IndexOf(';'),
                source.LastIndexOf(';'));
        tree.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Code == 1109 && diagnostic.Message == "Expression expected.");
    }

    [Fact]
    public void WhenVarianceModifiersAreReversed_ThenReportsTheirOrder()
    {
        var tree = TypeScriptParser.Parse("broken.ts", "export type Bad<out in T> = T;", TestContext.Current.CancellationToken);

        var diagnostic = tree.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be(1029);
        diagnostic.Message.Should().Be("'in' modifier must precede 'out' modifier.");
        diagnostic.Column.Should().Be(21);
    }

    [Fact]
    public void WhenTypeOnlyNamespaceExport_ThenRetainsTheAliasAndModule()
    {
        var tree = TypeScriptParser.Parse(
            "barrel.ts",
            "export type * as models from './model';",
            TestContext.Current.CancellationToken);

        tree.Diagnostics.Should().BeEmpty();
        var export = tree.Root.Statements.Should().ContainSingle().Which.Should().BeOfType<ExportDeclaration>().Subject;
        export.IsTypeOnly.Should().BeTrue();
        export.ModuleSpecifier.Should().BeOfType<StringLiteral>().Which.Text.Should().Be("./model");
        export.ExportClause
            .Should()
            .BeOfType<NamespaceExport>().Which.Name
            .Should()
            .BeOfType<Identifier>().Which.Text
            .Should()
            .Be("models");
    }

    [Fact]
    public void WhenNamedImportHasAlias_ThenRetainsBothNames()
    {
        var tree = TypeScriptParser.Parse(
            "app.ts",
            "import type { Model as LocalModel } from './model';",
            TestContext.Current.CancellationToken);

        tree.Diagnostics.Should().BeEmpty();
        var import = tree.Root.Statements.Should().ContainSingle().Which.Should().BeOfType<ImportDeclaration>().Subject;
        var clause = import.ImportClause.Should().BeOfType<ImportClause>().Subject;
        clause.IsTypeOnly.Should().BeTrue();
        var specifier = clause.NamedBindings
            .Should()
            .BeOfType<NamedImports>().Subject.Elements
            .Should()
            .ContainSingle().Subject;
        specifier.Name!.Text.Should().Be("LocalModel");
        specifier.PropertyName.Should().BeOfType<Identifier>().Which.Text.Should().Be("Model");
    }

    [Theory]
    [InlineData("broken.ts", "export interface Model {", 1005)]
    [InlineData("broken.ts", "export type * from ;", 1109)]
    [InlineData("broken.ts", "const value = '\\uQ';", 1125)]
    [InlineData("broken.tsx", "export const View = () => <div>A & B;", 17008)]
    public void WhenMalformedSource_ThenReportsASpecificDiagnostic(string fileName, string source, int code)
    {
        var tree = TypeScriptParser.Parse(fileName, source, TestContext.Current.CancellationToken);

        tree.HasErrors.Should().BeTrue();
        tree.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == code);
        tree.Diagnostics.Should().OnlyContain(diagnostic => diagnostic.Line >= 1 && diagnostic.Column >= 1 && diagnostic.Message.Length > 0);
    }

    [Fact]
    public void WhenParsingAfterFailure_ThenEarlierDiagnosticsRemainAndTheNextTreeIsValid()
    {
        var broken = TypeScriptParser.Parse("broken.ts", "export interface Model {", TestContext.Current.CancellationToken);
        var valid = TypeScriptParser.Parse("valid.ts", "export const value = 1;", TestContext.Current.CancellationToken);

        valid.Diagnostics.Should().BeEmpty();
        broken.Diagnostics.Should().ContainSingle().Which.Message.Should().Be("'}' expected.");
        broken.Root.FileName.Should().Be("broken.ts");
        valid.Root.FileName.Should().Be("valid.ts");
    }

    [Fact]
    public async Task WhenParsingConcurrentFiles_ThenKeepsSourcesAndDiagnosticsIsolated()
    {
        var trees = await Task.WhenAll(Enumerable.Range(0, 24).Select(index => Task.Run(
            () => TypeScriptParser.Parse(
                $"file{index}.ts",
                $"export const value{index} = {index};",
                TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken)));

        for (var index = 0; index < trees.Length; index++)
        {
            trees[index].Diagnostics.Should().BeEmpty();
            trees[index].Root.FileName.Should().Be($"file{index}.ts");
            var declaration = trees[index].Root
                .DescendantsAndSelf(TestContext.Current.CancellationToken)
                .OfType<VariableDeclaration>()
                .Should()
                .ContainSingle().Subject;
            declaration.Name.Should().BeOfType<Identifier>().Which.Text.Should().Be($"value{index}");
        }
    }

    [Fact]
    public void WhenCancelled_ThenPreservesCancellationAndTheNextParseSucceeds()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        var action = () => TypeScriptParser.Parse("source.ts", "export const value = 1;", cancellation.Token);

        action
            .Should()
            .ThrowExactly<OperationCanceledException>().Which.CancellationToken
            .Should()
            .Be(cancellation.Token);
        TypeScriptParser
            .Parse("source.ts", "export const value = 1;", TestContext.Current.CancellationToken).HasErrors
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WhenFileNameIsMissing_ThenRejectsTheInput(string fileName)
    {
        var action = () => TypeScriptParser.Parse(fileName, "", TestContext.Current.CancellationToken);

        action.Should().ThrowExactly<ArgumentException>().Which.ParamName.Should().Be("fileName");
    }

    [Fact]
    public void WhenSourceIsNull_ThenRejectsTheInput()
    {
        var action = () => TypeScriptParser.Parse("source.ts", null!, TestContext.Current.CancellationToken);

        action.Should().ThrowExactly<ArgumentNullException>().Which.ParamName.Should().Be("sourceText");
    }
}
