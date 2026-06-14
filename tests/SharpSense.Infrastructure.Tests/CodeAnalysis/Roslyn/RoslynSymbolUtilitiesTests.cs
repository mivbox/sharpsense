using System.Reflection;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpSense.Infrastructure.Persistence;

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

    private static string? ComputeBodyHash<TSyntaxNode>(string source)
        where TSyntaxNode : SyntaxNode
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var declaration = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<TSyntaxNode>()
            .Single();
        var utilitiesType = typeof(SharpSenseDbContext).Assembly.GetType(
            "SharpSense.Infrastructure.CodeAnalysis.Roslyn.RoslynSymbolUtilities",
            throwOnError: true)!;
        var computeBodyHash = utilitiesType.GetMethod(
            "ComputeBodyHash",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;

        return (string?)computeBodyHash.Invoke(null, [declaration]);
    }
}
