using AwesomeAssertions;
using SharpSense.TypeScript.Parsing;

namespace SharpSense.TypeScript.Tests.Parsing;

public sealed class ParseErrorAggregationTests
{
    [Fact]
    public void WhenNestedDeclarationHasSyntaxError_ThenItsAncestorsReportTheError()
    {
        var tree = TypeScriptParser.Parse(
            "source.ts",
            "export function run() { const broken = ; }",
            TestContext.Current.CancellationToken);
        tree.HasErrors.Should().BeTrue();

        var result = Utilities.ContainsParseError(tree.Root);

        result.Should().BeSameAs(tree.Root);
        Utilities.ContainsParseError(tree.Root).Should().BeSameAs(tree.Root);
    }

    [Fact]
    public void WhenErrorIsInsideADeepFlatExpression_ThenAggregatesWithoutRecursiveTraversal()
    {
        var source = "const value = " + string.Join(" + ", Enumerable.Repeat("1", 10_000)) + " + ;";
        var tree = TypeScriptParser.Parse("deep.ts", source, TestContext.Current.CancellationToken);

        var result = Utilities.ContainsParseError(tree.Root);

        tree.HasErrors.Should().BeTrue();
        result.Should().BeSameAs(tree.Root);
        Utilities.ContainsParseError(tree.Root).Should().BeSameAs(tree.Root);
    }

    [Fact]
    public void WhenNestedDeclarationsAreValid_ThenAggregationDoesNotInventErrors()
    {
        var tree = TypeScriptParser.Parse(
            "source.ts",
            "export function run() { const value = 1; }",
            TestContext.Current.CancellationToken);

        var result = Utilities.ContainsParseError(tree.Root);

        result.Should().BeNull();
        Utilities.ContainsParseError(tree.Root).Should().BeNull();
    }
}
