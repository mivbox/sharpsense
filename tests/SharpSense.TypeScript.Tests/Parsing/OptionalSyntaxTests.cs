using AwesomeAssertions;
using SharpSense.TypeScript.Parsing;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.TypeScript.Tests.Parsing;

public sealed class OptionalSyntaxTests
{
    [Fact]
    public void WhenOptionalSyntaxIsAbsent_ThenExposesNullChildrenAndTraversesRemainingNodes()
    {
        const string source = "import './setup'; function* run() { for (;;) { yield; return; } } const item = new Thing; try {} catch {}";

        var tree = TypeScriptParser.Parse("optional.ts", source, TestContext.Current.CancellationToken);
        var nodes = tree.Root.DescendantsAndSelf(TestContext.Current.CancellationToken).ToArray();

        tree.Diagnostics.Should().BeEmpty();
        nodes
            .OfType<ImportDeclaration>()
            .Single().ImportClause.Should().BeNull();
        nodes
            .OfType<ImportDeclaration>()
            .Single().Attributes.Should().BeNull();
        nodes
            .OfType<YieldExpression>()
            .Single().Expression.Should().BeNull();
        nodes
            .OfType<ReturnStatement>()
            .Single().Expression.Should().BeNull();
        nodes
            .OfType<ForStatement>()
            .Single().Initializer.Should().BeNull();
        nodes
            .OfType<NewExpression>()
            .Single().Arguments.Should().BeNull();
        nodes
            .OfType<CatchClause>()
            .Single().VariableDeclaration.Should().BeNull();
        nodes
            .OfType<TryStatement>()
            .Single().FinallyBlock.Should().BeNull();
    }

    [Fact]
    public void WhenJsxContainsOnlyAComment_ThenExpressionIsAbsentAndCommentIsPreserved()
    {
        var tree = TypeScriptParser.Parse(
            "view.tsx",
            "const view = <div>{/* comment */}</div>;",
            TestContext.Current.CancellationToken);

        var expression = tree.Root
            .DescendantsAndSelf(TestContext.Current.CancellationToken)
            .OfType<JsxExpression>()
            .Should()
            .ContainSingle().Subject;

        tree.Diagnostics.Should().BeEmpty();
        expression.Expression.Should().BeNull();
        tree.GetText(expression).Should().Be("{/* comment */}");
    }
}
