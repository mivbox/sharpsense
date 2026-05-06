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
        var codeNodesByCanonicalId = new Dictionary<string, CodeNode>(StringComparer.Ordinal);
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
                        symbolNodeIds,
                        projectId,
                        ct);
                }

                projectActivity.AddTag("project.code_node.count", codeNodesByCanonicalId.Count - codeNodeCountBeforeProject);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse project {ProjectName}", project.Name);
                projectActivity.RecordExceptionAndErrorStatus(ex);
                throw;
            }
        }

        var codeNodes = codeNodesByCanonicalId.Values
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            .ToArray();
        codeNodeActivity.AddTag("index.code_node.count", codeNodes.Length);

        return new NodeExtractionResult(codeNodes, declaredSymbols, symbolNodeIds);
    }

    public async Task<NodeExtractionResult> ExtractDocuments(
        IReadOnlyList<Document> documents,
        IRepositoryWorkspace repositoryWorkspace,
        IReadOnlyDictionary<ProjectId, string> projectIds,
        ConcurrentQueue<string> diagnostics,
        IProgress<IndexingProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(repositoryWorkspace);
        ArgumentNullException.ThrowIfNull(projectIds);
        ArgumentNullException.ThrowIfNull(diagnostics);

        using var codeNodeActivity = SharpSenseTraceSpan.Start("roslyn.build-code-nodes");
        var codeNodesByCanonicalId = new Dictionary<string, CodeNode>(StringComparer.Ordinal);
        var declaredSymbols = new List<DeclaredSymbolContext>();
        var symbolNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var orderedDocuments = documents
            .Where(static document => string.Equals(document.Project.Language, LanguageNames.CSharp, StringComparison.Ordinal))
            .OrderBy(static document => document.FilePath ?? document.Name, StringComparer.Ordinal)
            .ToArray();

        for (var documentIndex = 0; documentIndex < orderedDocuments.Length; documentIndex++)
        {
            var document = orderedDocuments[documentIndex];
            var currentCount = documentIndex + 1;
            progress?.Report(new IndexingProgress($"Parsing {document.Name}...", currentCount, orderedDocuments.Length));

            if (!projectIds.TryGetValue(document.Project.Id, out var projectId))
            {
                continue;
            }

            var syntaxRoot = await document.GetSyntaxRootAsync(ct);
            if (syntaxRoot is null)
            {
                diagnostics.Enqueue($"Unable to load the syntax tree for document '{document.Name}'.");
                continue;
            }

            var semanticModel = await document.GetSemanticModelAsync(ct);
            if (semanticModel is null)
            {
                diagnostics.Enqueue($"Unable to create a semantic model for document '{document.Name}'.");
                continue;
            }

            ExtractSyntaxRoot(
                document.Project,
                syntaxRoot.SyntaxTree,
                syntaxRoot,
                semanticModel,
                repositoryWorkspace,
                diagnostics,
                codeNodesByCanonicalId,
                declaredSymbols,
                symbolNodeIds,
                projectId);
        }

        var codeNodes = codeNodesByCanonicalId.Values
            .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
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
        IDictionary<string, CodeNode> codeNodesByCanonicalId,
        ICollection<DeclaredSymbolContext> declaredSymbols,
        IDictionary<string, string> symbolNodeIds,
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
            symbolNodeIds,
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
        IDictionary<string, string> symbolNodeIds,
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
                case ClassDeclarationSyntax classDeclaration:
                    AddDeclaredSymbol(
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        classDeclaration,
                        semanticModel.GetDeclaredSymbol(classDeclaration),
                        projectId,
                        relativeFilePath,
                        NodeType.Class);
                    break;

                case InterfaceDeclarationSyntax interfaceDeclaration:
                    AddDeclaredSymbol(
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        interfaceDeclaration,
                        semanticModel.GetDeclaredSymbol(interfaceDeclaration),
                        projectId,
                        relativeFilePath,
                        NodeType.Interface);
                    break;

                case RecordDeclarationSyntax recordDeclaration:
                    AddDeclaredSymbol(
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        recordDeclaration,
                        semanticModel.GetDeclaredSymbol(recordDeclaration),
                        projectId,
                        relativeFilePath,
                        NodeType.Class);
                    break;

                case StructDeclarationSyntax structDeclaration:
                    AddDeclaredSymbol(
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        symbolNodeIds,
                        semanticModel,
                        structDeclaration,
                        semanticModel.GetDeclaredSymbol(structDeclaration),
                        projectId,
                        relativeFilePath,
                        NodeType.Class);
                    break;

                case MethodDeclarationSyntax methodDeclaration:
                    AddDeclaredSymbol(
                        codeNodesByCanonicalId,
                        declaredSymbols,
                        symbolNodeIds,
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
                        symbolNodeIds,
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
                            symbolNodeIds,
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
        var canonicalId = RoslynSymbolUtilities.GetCanonicalId(projectId, canonicalSymbol);
        var (startLine, endLine) = GetSourceLineRange(declarationSyntax);

        symbolNodeIds.TryAdd(symbolLookupKey, canonicalId);
        symbolNodeIds.TryAdd(RoslynSymbolUtilities.GetLookupKey(symbol), canonicalId);

        if (!codeNodesByCanonicalId.ContainsKey(canonicalId))
        {
            var summary = RoslynSymbolUtilities.ExtractSummary(declarationSyntax);
            codeNodesByCanonicalId[canonicalId] = new CodeNode
            {
                CanonicalId = canonicalId,
                ProjectId = projectId,
                FullyQualifiedName = RoslynSymbolUtilities.GetFullyQualifiedName(canonicalSymbol),
                    DisplayName = RoslynSymbolUtilities.GetDisplayName(canonicalSymbol),
                NodeType = nodeType,
                RelativeFilePath = relativeFilePath,
                StartLine = startLine,
                EndLine = endLine,
                Summary = summary,
                SearchText = RoslynSymbolUtilities.BuildSearchText(canonicalSymbol, summary),
                BodyHash = RoslynSymbolUtilities.ComputeBodyHash(declarationSyntax)
            };
        }

        declaredSymbols.Add(new DeclaredSymbolContext(canonicalId, canonicalSymbol, declarationSyntax, semanticModel));
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
