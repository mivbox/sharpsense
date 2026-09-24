using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

public sealed class ImportDependencyPass(
    TsConfigResolver tsConfigResolver,
    IRepositoryWorkspace repositoryWorkspace) : ITypeScriptExtractionPass
{
    public void Execute(TypeScriptPassContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var knownNodeIds = context.CodeNodes
            .Select(static node => node.CanonicalId)
            .ToHashSet(StringComparer.Ordinal);
        var callerIdsByFile = context.CodeNodes
            .GroupBy(static node => node.RelativeFilePath, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static node => node.CanonicalId).ToArray(),
                StringComparer.Ordinal);
        var edgeIndexes = context.Edges
            .Select(static (edge, index) => new
            {
                Identity = (edge.CallerId, edge.CalleeId, edge.EdgeType),
                Index = index
            })
            .ToDictionary(static item => item.Identity, static item => item.Index);

        foreach (var parsedFile in context.ParsedFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!callerIdsByFile.TryGetValue(parsedFile.DiscoveredFile.RelativeFilePath, out var callerIds) ||
                callerIds.Length == 0)
            {
                continue;
            }

            var jsxTagNames = ExtractJsxTagNames(parsedFile);
            foreach (var importNode in parsedFile.RootNode.NamedChildren.Where(static node => node.Type == "import_statement"))
            {
                var importPath = importNode.GetChildForField("source")?.Text.Trim('\'', '"');
                if (string.IsNullOrWhiteSpace(importPath))
                {
                    continue;
                }

                var resolvedImportPath = tsConfigResolver.ResolveImport(importPath, parsedFile.DiscoveredFile.AbsolutePath, context.CancellationToken);
                IReadOnlyList<ResolvedImportTarget> targets;
                if (string.IsNullOrWhiteSpace(resolvedImportPath))
                {
                    if (IsRelativeImport(importPath))
                    {
                        continue;
                    }

                    targets = BuildExternalImportTargets(importNode, importPath, jsxTagNames);
                }
                else
                {
                    var targetRelativePath = repositoryWorkspace.ToRepositoryRelativePath(resolvedImportPath);
                    targets = BuildResolvedImportTargets(importNode, targetRelativePath, context);
                }

                foreach (var target in targets)
                {
                    if (!context.IsIncremental &&
                        !knownNodeIds.Contains(target.CalleeId) &&
                        !PackageNodeIdentity.IsPlaceholderId(target.CalleeId))
                    {
                        continue;
                    }

                    foreach (var callerId in callerIds)
                    {
                        if (string.Equals(callerId, target.CalleeId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var edgeIdentity = (callerId, target.CalleeId, EdgeType.Import);
                        if (edgeIndexes.ContainsKey(edgeIdentity))
                        {
                            continue;
                        }

                        edgeIndexes[edgeIdentity] = context.Edges.Count;
                        context.Edges.Add(new IndexedDependency(
                            callerId,
                            target.CalleeId,
                            EdgeType.Import,
                            target.Metadata));
                    }
                }
            }
        }
    }

    private IReadOnlyList<ResolvedImportTarget> BuildResolvedImportTargets(
        TreeSitter.Node importNode,
        string targetRelativePath,
        TypeScriptPassContext context)
    {
        var importedBindings = ExtractImportedBindings(importNode);
        if (importedBindings.Length == 0)
        {
            return [.. ResolveExports(context, targetRelativePath, null, new(StringComparer.Ordinal))
                .Select(id => new ResolvedImportTarget(id, null))];
        }

        var targetsByCalleeId = new Dictionary<string, ResolvedImportTarget>(StringComparer.Ordinal);
        foreach (var importedBinding in importedBindings)
        {
            if (importedBinding.IsNamespaceImport)
            {
                foreach (var id in ResolveExports(context, targetRelativePath, null, new(StringComparer.Ordinal)))
                {
                    AddResolvedTarget(targetsByCalleeId, new(id, null));
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(importedBinding.ExportName))
            {
                foreach (var id in ResolveExports(context, targetRelativePath, importedBinding.ExportName, new(StringComparer.Ordinal)))
                {
                    AddResolvedTarget(targetsByCalleeId, new(id, null));
                }
                continue;
            }

            foreach (var id in ResolveExports(context, targetRelativePath, "default", new(StringComparer.Ordinal)))
            {
                AddResolvedTarget(targetsByCalleeId, new(id, null));
            }
        }

        return [.. targetsByCalleeId.Values];
    }

    private IEnumerable<string> ResolveExports(
        TypeScriptPassContext context,
        string path,
        string? name,
        HashSet<string> visiting)
    {
        if (name is null)
        {
            foreach (var exportedName in GetExportNames(context, path, new(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal))
            {
                foreach (var id in ResolveExports(context, path, exportedName, visiting))
                {
                    yield return id;
                }
            }
            yield break;
        }

        var key = $"{path}\0{name}";
        if (!visiting.Add(key))
        {
            yield break;
        }
        try
        {
            if (context.ExportsByFile.TryGetValue(path, out var exports))
            {
                if (exports.TryGetValue(name, out var id))
                {
                    yield return id;
                    yield break; // Explicit local export wins over star exports.
                }
            }

            var source = context.ParsedFiles.FirstOrDefault(file => file.DiscoveredFile.RelativeFilePath == path);
            if (source is null)
            {
                yield break;
            }
            var reExports = context.ReExports.Where(binding => binding.FilePath == path).ToArray();
            var hasExplicitExport = reExports.Any(binding => binding.ExportedName == name);
            foreach (var binding in reExports)
            {
                if (hasExplicitExport && binding.ExportedName != name ||
                    binding.ExportedName is not null && binding.ExportedName != name)
                {
                    continue;
                }
                if (name == "default" && binding.ExportedName is null)
                {
                    continue;
                }
                var resolved = tsConfigResolver.ResolveImport(binding.Source, source.DiscoveredFile.AbsolutePath, context.CancellationToken);
                if (resolved is null)
                {
                    if (!IsRelativeImport(binding.Source) && binding.ImportedName is not null)
                    {
                        yield return PackageNodeIdentity.CreateCanonicalId(binding.Source, binding.ImportedName);
                    }
                    continue;
                }

                var relative = repositoryWorkspace.ToRepositoryRelativePath(resolved);
                var importedName = binding.ImportedName ?? (binding.ExportedName is null ? name : null);
                foreach (var id in ResolveExports(context, relative, importedName, visiting))
                {
                    yield return id;
                }
            }
        }
        finally
        {
            visiting.Remove(key);
        }
    }

    private IEnumerable<string> GetExportNames(TypeScriptPassContext context, string path, HashSet<string> visiting)
    {
        if (!visiting.Add(path))
        {
            yield break;
        }
        try
        {
            if (context.ExportsByFile.TryGetValue(path, out var exports))
            {
                foreach (var name in exports.Keys)
                {
                    yield return name;
                }
            }
            var source = context.ParsedFiles.FirstOrDefault(file => file.DiscoveredFile.RelativeFilePath == path);
            if (source is null)
            {
                yield break;
            }
            foreach (var binding in context.ReExports.Where(binding => binding.FilePath == path))
            {
                if (binding.ExportedName is not null)
                {
                    yield return binding.ExportedName;
                    continue;
                }
                var resolved = tsConfigResolver.ResolveImport(binding.Source, source.DiscoveredFile.AbsolutePath, context.CancellationToken);
                if (resolved is null)
                {
                    continue;
                }
                foreach (var name in GetExportNames(context, repositoryWorkspace.ToRepositoryRelativePath(resolved), visiting))
                {
                    if (name != "default")
                    {
                        yield return name;
                    }
                }
            }
        }
        finally
        {
            visiting.Remove(path);
        }
    }

    private static IReadOnlyList<ResolvedImportTarget> BuildExternalImportTargets(
        TreeSitter.Node importNode,
        string importPath,
        IReadOnlySet<string> jsxTagNames)
    {
        var importedBindings = ExtractImportedBindings(importNode);
        if (importedBindings.Length == 0)
        {
            return
            [
                new ResolvedImportTarget(
                    PackageNodeIdentity.CreateCanonicalId(importPath),
                    null)
            ];
        }

        var targetsByCalleeId = new Dictionary<string, ResolvedImportTarget>(StringComparer.Ordinal);

        foreach (var importedBinding in importedBindings)
        {
            if (importedBinding.IsNamespaceImport)
            {
                var matchingNamespaceTags = jsxTagNames
                    .Where(tagName =>
                        tagName.StartsWith($"{importedBinding.LocalName}.", StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (matchingNamespaceTags.Length > 0)
                {
                    foreach (var matchingNamespaceTag in matchingNamespaceTags)
                    {
                        var componentName = matchingNamespaceTag[(importedBinding.LocalName.Length + 1)..];
                        AddTarget(
                            targetsByCalleeId,
                            new ResolvedImportTarget(
                                PackageNodeIdentity.CreateCanonicalId(importPath, componentName),
                                null));
                    }

                    continue;
                }

                AddTarget(
                    targetsByCalleeId,
                    new ResolvedImportTarget(
                        PackageNodeIdentity.CreateCanonicalId(importPath),
                        null));
                continue;
            }

            var targetName = importedBinding.ExportName ?? importedBinding.LocalName;
            AddTarget(
                targetsByCalleeId,
                new ResolvedImportTarget(
                    PackageNodeIdentity.CreateCanonicalId(importPath, targetName),
                    null));
        }

        return [.. targetsByCalleeId.Values];
    }

    private static ImportedBinding[] ExtractImportedBindings(TreeSitter.Node importNode)
    {
        var clause = importNode.NamedChildren.FirstOrDefault(node => node.Type == "import_clause");
        if (clause is null)
        {
            return [];
        }

        var bindings = new List<ImportedBinding>();
        foreach (var child in clause.NamedChildren)
        {
            if (child.Type == "identifier")
            {
                bindings.Add(new(null, child.Text, false));
            }
            else if (child.Type == "namespace_import")
            {
                var local = child.NamedChildren.FirstOrDefault(node => node.Type == "identifier")?.Text;
                if (local is not null)
                {
                    bindings.Add(new(null, local, true));
                }
            }
            else if (child.Type == "named_imports")
            {
                foreach (var specifier in child.NamedChildren.Where(node => node.Type == "import_specifier"))
                {
                    var name = specifier.GetChildForField("name")?.Text.Trim('\'', '"');
                    var local = specifier.GetChildForField("alias")?.Text ?? name;
                    if (name is not null && local is not null)
                    {
                        bindings.Add(new(name, local, false));
                    }
                }
            }
        }
        return [.. bindings.Distinct()];
    }

    private static bool IsRelativeImport(string importPath)
        => importPath.StartsWith("./", StringComparison.Ordinal) ||
           importPath.StartsWith("../", StringComparison.Ordinal);

    private static IReadOnlySet<string> ExtractJsxTagNames(TypeScriptParsedFile parsedFile)
    {
        var jsxTagNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in EnumerateDescendants(parsedFile.RootNode))
        {
            if (node.Type is not "jsx_opening_element" and not "jsx_self_closing_element")
            {
                continue;
            }

            var nameNode = node.GetChildForField("name");
            if (nameNode is null)
            {
                foreach (var childNode in node.NamedChildren)
                {
                    nameNode = childNode;
                    break;
                }
            }

            var tagName = nameNode?.Text?.Trim();
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            jsxTagNames.Add(tagName);
        }

        return jsxTagNames;
    }

    private static IEnumerable<TreeSitter.Node> EnumerateDescendants(TreeSitter.Node node)
    {
        yield return node;

        foreach (var childNode in node.NamedChildren)
        {
            foreach (var descendantNode in EnumerateDescendants(childNode))
            {
                yield return descendantNode;
            }
        }
    }

    private static void AddTarget(
        IDictionary<string, ResolvedImportTarget> targetsByCalleeId,
        ResolvedImportTarget target)
    {
        if (targetsByCalleeId.ContainsKey(target.CalleeId))
        {
            return;
        }

        targetsByCalleeId[target.CalleeId] = target;
    }

    private static void AddResolvedTargets(
        IDictionary<string, ResolvedImportTarget> targetsByCalleeId,
        string targetRelativePath,
        IReadOnlyDictionary<string, string[]> targetNodeIdsByFile)
    {
        if (!targetNodeIdsByFile.TryGetValue(targetRelativePath, out var targetNodeIds))
        {
            return;
        }

        foreach (var targetNodeId in targetNodeIds)
        {
            AddResolvedTarget(targetsByCalleeId, new ResolvedImportTarget(targetNodeId, null));
        }
    }

    private static void AddResolvedTarget(
        IDictionary<string, ResolvedImportTarget> targetsByCalleeId,
        ResolvedImportTarget target)
        => AddTarget(targetsByCalleeId, target);

    private sealed record ImportedBinding(
        string? ExportName,
        string LocalName,
        bool IsNamespaceImport);

    private sealed record ResolvedImportTarget(
        string CalleeId,
        string? Metadata);
}
