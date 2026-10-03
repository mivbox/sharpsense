using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

public sealed class RoslynSymbolUtilitiesTests
{
    [Fact]
    public void WhenMethodTriviaChangesOnly_ThenBodyHashStaysStable()
    {
        var left = ComputeBodyHash<MethodDeclarationSyntax>(
            """
            class Sample
            {
                // comment
                string GetMessage()
                {
                    return "hello";
                }
            }
            """);
        var right = ComputeBodyHash<MethodDeclarationSyntax>(
            """
            class Sample
            {
                string GetMessage( )
                {

                    return "hello";
                }
            }
            """);

        left.Should().Be(right);
    }

    [Fact]
    public void WhenDeclarationBodyChanges_ThenBodyHashChanges()
    {
        var left = ComputeBodyHash<ClassDeclarationSyntax>(
            """
            class Sample
            {
                string GetMessage()
                {
                    return "hello";
                }
            }
            """);
        var right = ComputeBodyHash<ClassDeclarationSyntax>(
            """
            class Sample
            {
                string GetMessage()
                {
                    return "goodbye";
                }
            }
            """);

        left.Should().NotBe(right);
    }

    [Fact]
    public void WhenTokenBoundariesChange_ThenBodyHashChanges()
    {
        var before = ComputeBodyHash<MethodDeclarationSyntax>("class Sample { int M(int a, int b) => a + ++b; }");
        var after = ComputeBodyHash<MethodDeclarationSyntax>("class Sample { int M(int a, int b) => a++ + b; }");

        before.Should().NotBe(after);
    }

    private static string? ComputeBodyHash<TSyntaxNode>(string source)
        where TSyntaxNode : SyntaxNode
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var declaration = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TSyntaxNode>()
            .Single();

        return SharpSense.Infrastructure.CodeAnalysis.Roslyn.RoslynSymbolUtilities.ComputeBodyHash(declaration);
    }
}
