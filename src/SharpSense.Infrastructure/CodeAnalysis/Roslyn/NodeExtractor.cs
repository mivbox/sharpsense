using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Storage;
using System.Collections.Concurrent;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal sealed class NodeExtractor
{
    public async Task<NodeExtractionResult> Extract(
        IReadOnlyList<Project> orderedProjects,
        IRepositoryWorkspace repositoryWorkspace,
        IReadOnlyDictionary<ProjectId, string> projectIds,
        ConcurrentQueue<string> diagnostics,
        IProgress<IndexingProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(orderedProjects);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);
        ArgumentNullException.ThrowIfNull(projectIds);
        ArgumentNullException.ThrowIfNull(diagnostics);

        using var codeNodeActivity = SharpSenseTraceSpan.Start("roslyn.build-code-nodes");
        var codeNodesByCanonicalId = new Dictionary<string, CodeNode>(StringComparer.Ordinal);
        var declaredSymbols = new List<DeclaredSymbolContext>();

        for (var projectIndex = 0; projectIndex < orderedProjects.Count; projectIndex++)
        {
            var project = orderedProjects[projectIndex];
            var currentCount = projectIndex + 1;
            progress?.Report(new IndexingProgress(
                $"Parsing {project.Name}...",
                currentCount,
                orderedProjects.Count));

            using var projectActivity = SharpSenseTraceSpan.Start("roslyn.parse-project");
            projectActivity.AddTag("project.name", project.Name);
            projectActivity.AddTag("project.language", project.Language);
            projectActivity.AddTag("project.index", currentCount);
            projectActivity.AddTag("project.total", orderedProjects.Count);

            if (!projectIds.TryGetValue(project.Id, out var projectId))
            {
                projectActivity.AddTag("project.skipped", true);
                continue;
            }

            if (!string.Equals(project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
            {
                projectActivity.AddTag("project.skipped", true);
                projectActivity.AddTag("project.skip_reason", "unsupported-language");
                continue;
            }

            try
            {
                var compilation = await project.GetCompilationAsync(ct);
                if (compilation is null)
                {
                    diagnostics.Enqueue($"Unable to create a compilation for project '{project.Name}'.");
                    continue;
                }

                var syntaxTrees = compilation.SyntaxTrees
                    .OrderBy(static syntaxTree => syntaxTree.FilePath, StringComparer.Ordinal)
                    .ToArray();
                var codeNodeCountBeforeProject = codeNodesByCanonicalId.Count;

                projectActivity.AddTag("project.document.count", syntaxTrees.Length);

                foreach (var syntaxTree in syntaxTrees)
                {
                    ct.ThrowIfCancellationRequested();

                    await ExtractSyntaxTree(
                        project,
                        syntaxTree,
                        compilation,
                        repositoryWorkspace,
                        diagnostics,
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        projectId,
                        ct);
                }

                projectActivity.AddTag(
                    "project.code_node.count",
                    codeNodesByCanonicalId.Count - codeNodeCountBeforeProject);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse project {ProjectName}", project.Name);
                projectActivity.RecordExceptionAndErrorStatus(ex);
                throw;
            }
        }

        foreach (var declarations in declaredSymbols.GroupBy(declaration => declaration.NodeId, StringComparer.Ordinal))
        {
            codeNodesByCanonicalId[declarations.Key].BodyHash = RoslynSymbolUtilities.ComputeBodyHash(
                declarations.Select(declaration => GetHashSyntax(declaration.DeclarationSyntax)));
        }

        var codeNodes = codeNodesByCanonicalId.Values
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .ToArray();
        codeNodeActivity.AddTag("index.code_node.count", codeNodes.Length);

        return new NodeExtractionResult(codeNodes, declaredSymbols);
    }

    private static SyntaxNode GetHashSyntax(SyntaxNode syntax)
    {
        // A field shares its type, modifiers and attributes with its siblings, but
        // another variable's initializer must not invalidate this field's memories.
        if (syntax is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: BaseFieldDeclarationSyntax field } declaration } variable)
        {
            return field.WithDeclaration(declaration.WithVariables(
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory.SingletonSeparatedList(variable)));
        }

        return syntax;
    }

    private static async Task ExtractSyntaxTree(
        Project project,
        SyntaxTree syntaxTree,
        Compilation compilation,
        IRepositoryWorkspace repositoryWorkspace,
        ConcurrentQueue<string> diagnostics,
        IDictionary<string, CodeNode> codeNodesByCanonicalId,
        ICollection<DeclaredSymbolContext> declaredSymbols,
        string projectId,
        CancellationToken ct)
    {
        var syntaxRoot = await syntaxTree.GetRootAsync(ct);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        ExtractSyntaxRoot(
            project,
            syntaxTree,
            syntaxRoot,
            semanticModel,
            repositoryWorkspace,
            diagnostics,
            codeNodesByCanonicalId,
            declaredSymbols,
            projectId);
    }

    private static void ExtractSyntaxRoot(
        Project project,
        SyntaxTree syntaxTree,
        SyntaxNode syntaxRoot,
        SemanticModel semanticModel,
        IRepositoryWorkspace repositoryWorkspace,
        ConcurrentQueue<string> diagnostics,
        IDictionary<string, CodeNode> codeNodesByCanonicalId,
        ICollection<DeclaredSymbolContext> declaredSymbols,
        string projectId)
    {
        var relativeFilePath = RoslynPathUtilities.GetRelativeSyntaxTreePath(
            project,
            syntaxTree,
            repositoryWorkspace,
            diagnostics);
        if (relativeFilePath is null)
        {
            return;
        }

        foreach (var syntaxNode in syntaxRoot.DescendantNodes())
        {
            switch (syntaxNode)
            {
                case TypeDeclarationSyntax typeDeclaration:
                    AddDeclaredSymbol(
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        semanticModel,
                        typeDeclaration,
                        semanticModel.GetDeclaredSymbol(typeDeclaration),
                        projectId,
                        relativeFilePath,
                        typeDeclaration is InterfaceDeclarationSyntax ? NodeType.Interface : NodeType.Class);
                    break;

                case MethodDeclarationSyntax methodDeclaration:
                    AddDeclaredSymbol(
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        semanticModel,
                        methodDeclaration,
                        semanticModel.GetDeclaredSymbol(methodDeclaration),
                        projectId,
                        relativeFilePath,
                        NodeType.Method);
                    break;

                case PropertyDeclarationSyntax propertyDeclaration:
                    AddDeclaredSymbol(
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        semanticModel,
                        propertyDeclaration,
                        semanticModel.GetDeclaredSymbol(propertyDeclaration),
                        projectId,
                        relativeFilePath,
                        NodeType.Property);
                    break;

                case FieldDeclarationSyntax fieldDeclaration:
                    foreach (var variable in fieldDeclaration.Declaration.Variables)
                    {
                        AddDeclaredSymbol(
                            codeNodesByCanonicalId,
                            declaredSymbols,
                            semanticModel,
                            variable,
                            semanticModel.GetDeclaredSymbol(variable),
                            projectId,
                            relativeFilePath,
                            NodeType.Field);
                    }

                    break;
            }
        }
    }

    private static void AddDeclaredSymbol(
        IDictionary<string, CodeNode> codeNodesByCanonicalId,
        ICollection<DeclaredSymbolContext> declaredSymbols,
        SemanticModel semanticModel,
        SyntaxNode declarationSyntax,
        ISymbol? symbol,
        string projectId,
        string relativeFilePath,
        NodeType nodeType)
    {
        if (symbol is null)
        {
            return;
        }

        var canonicalSymbol = RoslynSymbolUtilities.Canonicalize(symbol);
        var canonicalId = RoslynSymbolUtilities.GetCanonicalId(projectId, canonicalSymbol);
        var fullyQualifiedName = RoslynSymbolUtilities.GetFullyQualifiedName(canonicalSymbol);
        var (startLine, endLine) = GetSourceLineRange(declarationSyntax);


        // Partial declarations share an identity within their declaring project.
        // Identically named declarations in different projects remain distinct.
        if (!codeNodesByCanonicalId.ContainsKey(canonicalId))
        {
            var summary = RoslynSymbolUtilities.ExtractSummary(declarationSyntax);
            codeNodesByCanonicalId[canonicalId] = new CodeNode
            {
                CanonicalId = canonicalId,
                ProjectId = projectId,
                FullyQualifiedName = fullyQualifiedName,
                DisplayName = RoslynSymbolUtilities.GetDisplayName(canonicalSymbol),
                NodeType = nodeType,
                RelativeFilePath = relativeFilePath,
                StartLine = startLine,
                EndLine = endLine,
                Summary = summary,
                SearchText = RoslynSymbolUtilities.BuildSearchText(canonicalSymbol, summary)
            };
        }

        declaredSymbols.Add(new DeclaredSymbolContext(
            canonicalId,
            projectId,
            canonicalSymbol,
            declarationSyntax,
            semanticModel));
    }

    private static (int StartLine, int EndLine) GetSourceLineRange(SyntaxNode declarationSyntax)
    {
        var lineSpan = declarationSyntax.GetLocation()
            .GetLineSpan();

        return (
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.EndLinePosition.Line + 1);
    }
}
