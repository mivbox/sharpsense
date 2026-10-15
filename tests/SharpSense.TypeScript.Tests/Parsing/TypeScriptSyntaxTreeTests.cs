using AwesomeAssertions;
using SharpSense.TypeScript.Parsing;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.TypeScript.Tests.Parsing;

public sealed class TypeScriptSyntaxTreeTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void WhenReadingSourceLines_ThenPreservesTextAndEmptyLinesWithoutTerminators(string newline)
    {
        var source = "// 😀 first" + newline + newline + "// last";

        var tree = TypeScriptParser.Parse("lines.ts", source, TestContext.Current.CancellationToken);

        tree.LineCount.Should().Be(3);
        tree.GetLineText(1).ToString().Should().Be("// 😀 first");
        tree.GetLineText(2).IsEmpty.Should().BeTrue();
        tree.GetLineText(3).ToString().Should().Be("// last");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WhenLineIsOutsideSource_ThenRejectsTheLine(int line)
    {
        var tree = TypeScriptParser.Parse("line.ts", "// comment", TestContext.Current.CancellationToken);

        var read = () => tree.GetLineText(line).ToString();

        read.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("line");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void WhenMissingTokenFollowsUnicode_ThenReportsOneBasedUtf16Position(string newline)
    {
        var lastLine = "export function run() { const text = '😀é';";
        var source = "// header" + newline + lastLine;

        var tree = TypeScriptParser.Parse("source.ts", source, TestContext.Current.CancellationToken);

        var diagnostic = tree.Diagnostics.Should().ContainSingle().Subject;
        diagnostic.Code.Should().Be(1005);
        diagnostic.Message.Should().Be("'}' expected.");
        diagnostic.Start.Should().Be(source.Length);
        diagnostic.Length.Should().Be(0);
        diagnostic.Line.Should().Be(2);
        diagnostic.Column.Should().Be(lastLine.Length + 1);
        tree.GetPosition(source.Length).Should().Be(new SourcePosition(2, lastLine.Length + 1));
    }

    [Fact]
    public void WhenSourceHasLeadingComments_ThenNodeTextExcludesTriviaAndRetainsUnicode()
    {
        const string source = "// café\n/** Documentation. */\nexport const value = '😀';";
        var tree = TypeScriptParser.Parse("source.ts", source, TestContext.Current.CancellationToken);
        var declaration = tree.Root
            .DescendantsAndSelf(TestContext.Current.CancellationToken)
            .OfType<VariableDeclaration>()
            .Should()
            .ContainSingle().Subject;

        tree.GetText(declaration).Should().Be("value = '😀'");
        tree.GetPosition(tree.GetStart(declaration)).Should().Be(new SourcePosition(3, 14));
    }

    [Fact]
    public void WhenWalkingSyntax_ThenVisitsNestedCallsOnceInSourceOrder()
    {
        var tree = TypeScriptParser.Parse(
            "source.ts",
            "export function run() { first(second()); third(); }",
            TestContext.Current.CancellationToken);

        var calls = tree.Root.DescendantsAndSelf(TestContext.Current.CancellationToken)
            .OfType<CallExpression>()
            .Select(tree.GetText)
            .ToArray();

        calls.Should().Equal("first(second())", "second()", "third()");
    }

    [Fact]
    public void WhenTraversalIsCancelled_ThenStopsEnumerating()
    {
        var tree = TypeScriptParser.Parse("source.ts", "export const value = 1;", TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var nodes = tree.Root.DescendantsAndSelf(cancellation.Token).GetEnumerator();
        nodes.MoveNext().Should().BeTrue();
        cancellation.Cancel();

        var action = () => nodes.MoveNext();

        action.Should().ThrowExactly<OperationCanceledException>();
    }

    [Fact]
    public void WhenSourceIsEmpty_ThenReturnsAnEmptyFileAndItsPosition()
    {
        var tree = TypeScriptParser.Parse("empty.ts", "", TestContext.Current.CancellationToken);

        tree.Diagnostics.Should().BeEmpty();
        tree.Root.Statements.Should().BeEmpty();
        tree.GetPosition(0).Should().Be(new SourcePosition(1, 1));
        tree.LineCount.Should().Be(1);
        tree.GetLineText(1).IsEmpty.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void WhenPositionIsOutsideSource_ThenRejectsTheOffset(int offset)
    {
        var tree = TypeScriptParser.Parse("source.ts", " ", TestContext.Current.CancellationToken);

        var action = () => tree.GetPosition(offset);

        action.Should().ThrowExactly<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("offset");
    }
}
