using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Serilog;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Storage;

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
        var codeNodesById = new Dictionary<string, CodeNode>(StringComparer.Ordinal);
        var declaredSymbols = new List<DeclaredSymbolContext>();
        var symbolNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var projectIndex = 0; projectIndex < orderedProjects.Count; projectIndex++)
        {
            var project = orderedProjects[projectIndex];
            var currentCount = projectIndex + 1;
            progress?.Report(new IndexingProgress($"Parsing {project.Name}...", currentCount, orderedProjects.Count));

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
                var codeNodeCountBeforeProject = codeNodesById.Count;

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
                        codeNodesById,
                        declaredSymbols,
                        symbolNodeIds,
                        projectId,
                        ct);
                }

                projectActivity.AddTag("project.code_node.count", codeNodesById.Count - codeNodeCountBeforeProject);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse project {ProjectName}", project.Name);
                projectActivity.RecordExceptionAndErrorStatus(ex);
                throw;
            }
        }

        var codeNodes = codeNodesById.Values
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.Id, StringComparer.Ordinal)
            .ToArray();
        codeNodeActivity.AddTag("index.code_node.count", codeNodes.Length);

        return new NodeExtractionResult(codeNodes, declaredSymbols, symbolNodeIds);
    }

    private static async Task ExtractSyntaxTree(
        Project project,
        SyntaxTree syntaxTree,
        Compilation compilation,
        IRepositoryWorkspace repositoryWorkspace,
        ConcurrentQueue<string> diagnostics,
        IDictionary<string, CodeNode> codeNodesById,
        ICollection<DeclaredSymbolContext> declaredSymbols,
        IDictionary<string, string> symbolNodeIds,
        string projectId,
        CancellationToken ct)
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

        var syntaxRoot = await syntaxTree.GetRootAsync(ct);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);

        foreach (var syntaxNode in syntaxRoot.DescendantNodes())
        {
            switch (syntaxNode)
            {
                case ClassDeclarationSyntax classDeclaration:
                    AddDeclaredSymbol(
                        codeNodesById,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        classDeclaration,
                        semanticModel.GetDeclaredSymbol(classDeclaration, ct),
                        projectId,
                        relativeFilePath,
                        NodeType.Class);
                    break;

                case InterfaceDeclarationSyntax interfaceDeclaration:
                    AddDeclaredSymbol(
                        codeNodesById,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        interfaceDeclaration,
                        semanticModel.GetDeclaredSymbol(interfaceDeclaration, ct),
                        projectId,
                        relativeFilePath,
                        NodeType.Interface);
                    break;

                case RecordDeclarationSyntax recordDeclaration:
                    AddDeclaredSymbol(
                        codeNodesById,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        recordDeclaration,
                        semanticModel.GetDeclaredSymbol(recordDeclaration, ct),
                        projectId,
                        relativeFilePath,
                        NodeType.Class);
                    break;

                case MethodDeclarationSyntax methodDeclaration:
                    AddDeclaredSymbol(
                        codeNodesById,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        methodDeclaration,
                        semanticModel.GetDeclaredSymbol(methodDeclaration, ct),
                        projectId,
                        relativeFilePath,
                        NodeType.Method);
                    break;

                case PropertyDeclarationSyntax propertyDeclaration:
                    AddDeclaredSymbol(
                        codeNodesById,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        propertyDeclaration,
                        semanticModel.GetDeclaredSymbol(propertyDeclaration, ct),
                        projectId,
                        relativeFilePath,
                        NodeType.Property);
                    break;

                case FieldDeclarationSyntax fieldDeclaration:
                    foreach (var variable in fieldDeclaration.Declaration.Variables)
                    {
                        AddDeclaredSymbol(
                            codeNodesById,
                            declaredSymbols,
                            symbolNodeIds,
                            semanticModel,
                            variable,
                            semanticModel.GetDeclaredSymbol(variable, ct),
                            projectId,
                            relativeFilePath,
                            NodeType.Field);
                    }

                    break;
            }
        }
    }

    private static void AddDeclaredSymbol(
        IDictionary<string, CodeNode> codeNodesById,
        ICollection<DeclaredSymbolContext> declaredSymbols,
        IDictionary<string, string> symbolNodeIds,
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
        var symbolLookupKey = RoslynSymbolUtilities.GetLookupKey(canonicalSymbol);
        var nodeId = RoslynSymbolUtilities.GetNodeId(projectId, canonicalSymbol);
        var (startLine, endLine) = GetSourceLineRange(declarationSyntax);

        symbolNodeIds.TryAdd(symbolLookupKey, nodeId);
        symbolNodeIds.TryAdd(RoslynSymbolUtilities.GetLookupKey(symbol), nodeId);

        if (!codeNodesById.ContainsKey(nodeId))
        {
            codeNodesById[nodeId] = new CodeNode
            {
                Id = nodeId,
                ProjectId = projectId,
                FullyQualifiedName = RoslynSymbolUtilities.GetFullyQualifiedName(canonicalSymbol),
                NodeType = nodeType,
                RelativeFilePath = relativeFilePath,
                StartLine = startLine,
                EndLine = endLine,
                Summary = RoslynSymbolUtilities.ExtractSummary(canonicalSymbol, nodeType)
            };
        }

        declaredSymbols.Add(new DeclaredSymbolContext(nodeId, canonicalSymbol, declarationSyntax, semanticModel));
    }

    private static (int StartLine, int EndLine) GetSourceLineRange(SyntaxNode declarationSyntax)
    {
        var lineSpan = declarationSyntax.GetLocation().GetLineSpan();
        return (
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.EndLinePosition.Line + 1);
    }
}

internal sealed record NodeExtractionResult(
    IReadOnlyList<CodeNode> CodeNodes,
    IReadOnlyList<DeclaredSymbolContext> DeclaredSymbols,
    IReadOnlyDictionary<string, string> SymbolNodeIds);
