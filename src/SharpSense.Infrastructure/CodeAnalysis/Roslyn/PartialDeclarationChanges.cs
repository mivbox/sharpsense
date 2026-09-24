using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpSense.Application.Indexing.Models;

namespace SharpSense.Infrastructure.CodeAnalysis.Roslyn;

internal static class PartialDeclarationChanges
{
    // A persisted symbol has one identity and one source location even when its declarations
    // span files. Re-extract complete related documents so identity, members and edges agree.
    public static async Task<IReadOnlyList<WorkspaceFileChange>> Expand(
        Solution solution,
        IReadOnlyList<WorkspaceFileChange> changes,
        CancellationToken ct)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var affectedPaths = changes.SelectMany(static change => change.GetAffectedPaths()).ToHashSet(comparer);
        var documentsByPath = solution.Projects.SelectMany(static project => project.Documents)
            .Where(static document => document.FilePath is not null)
            .GroupBy(static document => document.FilePath!, comparer)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), comparer);
        var pending = new Queue<Document>(affectedPaths
            .Where(documentsByPath.ContainsKey)
            .SelectMany(path => documentsByPath[path]));
        var visited = new HashSet<DocumentId>();
        var expandedChanges = changes.ToList();

        while (pending.TryDequeue(out var document))
        {
            if (!visited.Add(document.Id))
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(ct);
            if (root is null)
            {
                continue;
            }

            var declarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>()
                .Where(static declaration => declaration.Modifiers.Any(static modifier => modifier.ValueText == "partial"))
                .ToArray();
            if (declarations.Length == 0)
            {
                continue;
            }

            var model = await document.GetSemanticModelAsync(ct);
            if (model is null)
            {
                continue;
            }

            foreach (var declaration in declarations)
            {
                var symbol = model.GetDeclaredSymbol(declaration, ct);
                if (symbol is null)
                {
                    continue;
                }

                foreach (var reference in symbol.DeclaringSyntaxReferences)
                {
                    var path = reference.SyntaxTree.FilePath;
                    if (!documentsByPath.TryGetValue(path, out var relatedDocuments) || !affectedPaths.Add(path))
                    {
                        continue;
                    }

                    expandedChanges.Add(new WorkspaceFileChange(WorkspaceFileChangeAction.Modified, NewPath: path));
                    foreach (var relatedDocument in relatedDocuments)
                    {
                        pending.Enqueue(relatedDocument);
                    }
                }
            }
        }

        return expandedChanges;
    }
}
