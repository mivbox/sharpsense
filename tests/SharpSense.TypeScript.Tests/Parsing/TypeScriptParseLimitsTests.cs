using AwesomeAssertions;
using SharpSense.TypeScript.Parsing;
using SharpSense.TypeScript.Parsing.Models;
using SharpSense.TypeScript.Scanning;

namespace SharpSense.TypeScript.Tests.Parsing;

public sealed class TypeScriptParseLimitsTests
{
    [Theory]
    [InlineData("const value = ", "(", "0", ")", ";")]
    [InlineData("type Value = ", "Array<", "string", ">", ";")]
    [InlineData("", "if (true) ", "run();", "", "")]
    [InlineData("const value = ", "!", "true", "", ";")]
    public void WhenRecursionLimitIsExceeded_ThenFailsWithoutReturningAPartialTree(
        string prefix,
        string open,
        string value,
        string close,
        string suffix)
    {
        var source = prefix + string.Concat(Enumerable.Repeat(open, 200)) + value +
            string.Concat(Enumerable.Repeat(close, 200)) + suffix;
        var options = new TypeScriptParseOptions
        {
            MaxRecursionDepth = 64
        };

        var parse = () => TypeScriptParser.Parse("deep.ts", source, options, TestContext.Current.CancellationToken);

        parse
            .Should()
            .Throw<TypeScriptParseLimitException>().Which.Limit
            .Should()
            .Be(nameof(options.MaxRecursionDepth));
        TypeScriptParser
            .Parse("valid.ts", "export const value = 1;", TestContext.Current.CancellationToken).HasErrors
            .Should()
            .BeFalse();
    }

    [Fact]
    public void WhenRegexValidationExceedsRecursionLimit_ThenStopsWithinTheScannerBudget()
    {
        var scanner = new Scanner(
            ScriptTarget.Latest,
            skipTrivia: true,
            textInitial: "/" + new string('(', 200) + "a" + new string(')', 200) + "/u",
            budget: new ParseBudget(
                new TypeScriptParseOptions
                {
                    MaxRecursionDepth = 64
                },
                TestContext.Current.CancellationToken));
        scanner.Scan();

        var scan = () => scanner.ReScanSlashToken(reportErrors: true);

        scan
            .Should()
            .Throw<TypeScriptParseLimitException>().Which.Limit
            .Should()
            .Be(nameof(TypeScriptParseOptions.MaxRecursionDepth));
    }

    [Fact]
    public void WhenNodeBudgetIsExhausted_ThenReportsTheLimitAndSourceOffset()
    {
        const string source = "const a = 1; const b = 2; const c = 3;";
        var options = new TypeScriptParseOptions
        {
            MaxNodeCount = 5
        };

        var parse = () => TypeScriptParser.Parse("many.ts", source, options, TestContext.Current.CancellationToken);

        var error = parse.Should().Throw<TypeScriptParseLimitException>().Which;
        error.Limit.Should().Be(nameof(options.MaxNodeCount));
        error.Offset.Should().BeInRange(0, source.Length);
    }

    [Fact]
    public void WhenSourceLengthExceedsTheLimit_ThenRejectsBeforeParsing()
    {
        var options = new TypeScriptParseOptions
        {
            MaxSourceLength = 3
        };

        var parse = () => TypeScriptParser.Parse("large.ts", "    ", options, TestContext.Current.CancellationToken);

        parse
            .Should()
            .Throw<TypeScriptParseLimitException>().Which.Limit
            .Should()
            .Be(nameof(options.MaxSourceLength));
        TypeScriptParser
            .Parse("small.ts", "   ", options, TestContext.Current.CancellationToken).HasErrors
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(0, 256, 100)]
    [InlineData(100, 0, 100)]
    [InlineData(100, 513, 100)]
    [InlineData(100, 256, 0)]
    public void WhenLimitsAreInvalid_ThenRejectsTheOptions(int length, int depth, int nodes)
    {
        var options = new TypeScriptParseOptions
        {
            MaxSourceLength = length,
            MaxRecursionDepth = depth,
            MaxNodeCount = nodes
        };

        var parse = () => TypeScriptParser.Parse("file.ts", "", options, TestContext.Current.CancellationToken);

        parse.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WhenCancellationOccursInsideAString_ThenStopsBeforeTheTokenCompletes()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scanner = new Scanner(
            ScriptTarget.Latest,
            skipTrivia: true,
            textInitial: "'\\uQ" + new string('a', 1_000_000) + "'",
            onError: (_, _, _) => cancellation.Cancel(),
            budget: new ParseBudget(new TypeScriptParseOptions(), cancellation.Token));

        var scan = () => scanner.Scan();

        scan
            .Should()
            .Throw<OperationCanceledException>().Which.CancellationToken
            .Should()
            .Be(cancellation.Token);
        scanner.GetTokenEnd().Should().BeLessThan(100);
    }

    [Fact]
    public void WhenFlatExpressionProducesADeepTree_ThenLinksAndTraversesWithoutRecursiveStackGrowth()
    {
        var source = "export const sum = " + string.Join(" + ", Enumerable.Repeat("1", 10_000)) + ";";

        var tree = TypeScriptParser.Parse("sum.ts", source, TestContext.Current.CancellationToken);
        var literals = tree.Root.DescendantsAndSelf(TestContext.Current.CancellationToken)
            .OfType<NumericLiteral>()
            .ToArray();

        tree.Diagnostics.Should().BeEmpty();
        literals.Should().HaveCount(10_000);
        literals.Should().OnlyContain(literal => literal.Parent is BinaryExpression);
        tree.Root
            .ForEachChild(node => node is NumericLiteral ? node : null, recursively: true)
            .Should()
            .BeSameAs(literals[0]);
    }
}
