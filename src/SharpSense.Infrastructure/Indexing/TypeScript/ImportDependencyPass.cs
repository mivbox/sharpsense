using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Storage;
using SharpSense.TypeScript.Parsing.Abstractions;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

internal sealed class ImportDependencyPass(
    TsConfigResolver tsConfigResolver,
    IRepositoryWorkspace repositoryWorkspace) : ITypeScriptExtractionPass
{
    public void Execute(TypeScriptPassContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var knownNodeIds = context.CodeNodes
            .Select(static node => node.CanonicalId)
            .ToHashSet(StringComparer.Ordinal);
        var memberIds = context.ParentEdges
            .Where(edge => knownNodeIds.Contains(edge.ParentId))
            .Select(static edge => edge.MemberId)
            .ToHashSet(StringComparer.Ordinal);
        var callerIdsByFile = context.CodeNodes
            .GroupBy(static node => node.RelativeFilePath, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static node => node.CanonicalId)
                    .ToArray(),
                StringComparer.Ordinal);
        var edgeIdentities = context.Edges
            .Select(static edge => (edge.CallerId, edge.CalleeId, edge.EdgeType))
            .ToHashSet();

        foreach (var parsedFile in context.ParsedFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var filePath = parsedFile.DiscoveredFile.RelativeFilePath;
            var localImports = context.LocalImportsByFile[filePath] = new(StringComparer.Ordinal);
            if (!callerIdsByFile.TryGetValue(filePath, out var callerIds) ||
                callerIds.Length == 0)
            {
                CollectLocalImportsWithoutCallers(parsedFile, context, localImports);
                continue;
            }

            var topLevelCallerIds = callerIds
                .Where(id => !memberIds.Contains(id))
                .ToArray();
            var referencedIdentifiersByCaller = BuildReferencedIdentifiersByCaller(
                parsedFile,
                callerIds,
                context);
            var jsxTagNamesByCaller = BuildJsxTagNamesByCaller(parsedFile, callerIds, context);

            foreach (var importNode in (parsedFile.RootNode.Statements ?? [])
                .OfType<ImportDeclaration>())
            {
                var importPath = TypeScriptSyntax.GetModuleSpecifier(importNode);
                if (string.IsNullOrWhiteSpace(importPath))
                {
                    continue;
                }

                var resolvedImportPath = tsConfigResolver.ResolveImport(
                    importPath,
                    parsedFile.DiscoveredFile.AbsolutePath,
                    context.CancellationToken);
                var importedBindings = TypeScriptSyntax.ReadImports(importNode)
                    .Distinct()
                    .ToArray();

                if (string.IsNullOrWhiteSpace(resolvedImportPath))
                {
                    if (IsRelativeImport(importPath))
                    {
                        continue;
                    }

                    AddExternalImportEdges(
                        context,
                        importPath,
                        importedBindings,
                        callerIds,
                        topLevelCallerIds,
                        referencedIdentifiersByCaller,
                        jsxTagNamesByCaller,
                        edgeIdentities);
                    continue;
                }

                var targetRelativePath = repositoryWorkspace.ToRepositoryRelativePath(resolvedImportPath);
                AddResolvedImportEdges(
                    context,
                    targetRelativePath,
                    importedBindings,
                    callerIds,
                    topLevelCallerIds,
                    referencedIdentifiersByCaller,
                    knownNodeIds,
                    edgeIdentities,
                    localImports);
            }

            AddDynamicImportBindingEdges(
                parsedFile,
                context,
                callerIds,
                topLevelCallerIds,
                referencedIdentifiersByCaller,
                knownNodeIds,
                edgeIdentities,
                localImports);
        }
    }

    private void AddDynamicImportBindingEdges(
        TypeScriptParsedFile parsedFile,
        TypeScriptPassContext context,
        string[] callerIds,
        string[] topLevelCallerIds,
        Dictionary<string, HashSet<string>> referencedIdentifiersByCaller,
        HashSet<string> knownNodeIds,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeIdentities,
        Dictionary<string, ResolvedLocalImportBinding> localImports)
    {
        foreach (var statement in (parsedFile.RootNode.Statements ?? []).OfType<VariableStatement>())
        {
            foreach (var variable in statement.DeclarationList.Declarations ?? [])
            {
                if (variable.Name is not Identifier { Text: { } localName } ||
                    variable.Initializer is null)
                {
                    continue;
                }

                var dynamicSpecifiers = variable.Initializer
                    .DescendantsAndSelf(context.CancellationToken)
                    .OfType<CallExpression>()
                    .Select(static call => TypeScriptSyntax.TryGetDynamicImportSpecifier(call, out var specifier)
                        ? specifier
                        : null)
                    .Where(static specifier => !string.IsNullOrWhiteSpace(specifier))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (dynamicSpecifiers.Length != 1)
                {
                    continue;
                }

                var importPath = dynamicSpecifiers[0]!;
                var resolvedImportPath = tsConfigResolver.ResolveImport(
                    importPath,
                    parsedFile.DiscoveredFile.AbsolutePath,
                    context.CancellationToken);
                if (string.IsNullOrWhiteSpace(resolvedImportPath))
                {
                    continue;
                }

                var targetRelativePath = repositoryWorkspace.ToRepositoryRelativePath(resolvedImportPath);
                var defaultTargets = ResolveExports(context, targetRelativePath, "default", new(StringComparer.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                var targetIds = defaultTargets.Length > 0
                    ? defaultTargets
                    : [.. ResolveExports(context, targetRelativePath, null, new(StringComparer.Ordinal)).Distinct(StringComparer.Ordinal)];
                if (targetIds.Length == 0)
                {
                    continue;
                }

                localImports[localName] = new ResolvedLocalImportBinding(
                    localName,
                    IsNamespaceImport: false,
                    targetRelativePath,
                    targetIds);

                var matchingCallers = GetReferencingCallers(
                    callerIds,
                    topLevelCallerIds,
                    referencedIdentifiersByCaller,
                    localName);
                foreach (var calleeId in targetIds)
                {
                    AddImportEdgesToCallers(context, matchingCallers, calleeId, null, knownNodeIds, edgeIdentities);
                }
            }
        }
    }

    private void CollectLocalImportsWithoutCallers(
        TypeScriptParsedFile parsedFile,
        TypeScriptPassContext context,
        Dictionary<string, ResolvedLocalImportBinding> localImports)
    {
        foreach (var importNode in (parsedFile.RootNode.Statements ?? []).OfType<ImportDeclaration>())
        {
            var importPath = TypeScriptSyntax.GetModuleSpecifier(importNode);
            if (string.IsNullOrWhiteSpace(importPath))
            {
                continue;
            }

            var resolvedImportPath = tsConfigResolver.ResolveImport(
                importPath,
                parsedFile.DiscoveredFile.AbsolutePath,
                context.CancellationToken);
            if (string.IsNullOrWhiteSpace(resolvedImportPath))
            {
                continue;
            }

            var targetRelativePath = repositoryWorkspace.ToRepositoryRelativePath(resolvedImportPath);
            foreach (var binding in TypeScriptSyntax.ReadImports(importNode).Distinct())
            {
                var targets = ResolveBindingTargetIds(context, targetRelativePath, binding);
                localImports[binding.LocalName] = new ResolvedLocalImportBinding(
                    binding.LocalName,
                    binding.IsNamespaceImport,
                    targetRelativePath,
                    targets);
            }
        }
    }

    private void AddResolvedImportEdges(
        TypeScriptPassContext context,
        string targetRelativePath,
        TypeScriptImportBinding[] importedBindings,
        string[] callerIds,
        string[] topLevelCallerIds,
        Dictionary<string, HashSet<string>> referencedIdentifiersByCaller,
        HashSet<string> knownNodeIds,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeIdentities,
        Dictionary<string, ResolvedLocalImportBinding> localImports)
    {
        if (importedBindings.Length == 0)
        {
            var sideEffectTargets = ResolveExports(context, targetRelativePath, null, new(StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal);
            foreach (var calleeId in sideEffectTargets)
            {
                AddImportEdgesToCallers(context, topLevelCallerIds, calleeId, null, knownNodeIds, edgeIdentities);
            }

            return;
        }

        foreach (var importedBinding in importedBindings)
        {
            var targetIds = ResolveBindingTargetIds(context, targetRelativePath, importedBinding);
            localImports[importedBinding.LocalName] = new ResolvedLocalImportBinding(
                importedBinding.LocalName,
                importedBinding.IsNamespaceImport,
                targetRelativePath,
                targetIds);

            var matchingCallers = GetReferencingCallers(
                callerIds,
                topLevelCallerIds,
                referencedIdentifiersByCaller,
                importedBinding.LocalName);
            foreach (var calleeId in targetIds)
            {
                AddImportEdgesToCallers(context, matchingCallers, calleeId, null, knownNodeIds, edgeIdentities);
            }
        }
    }

    private string[] ResolveBindingTargetIds(
        TypeScriptPassContext context,
        string targetRelativePath,
        TypeScriptImportBinding importedBinding)
    {
        var exportName = importedBinding.IsNamespaceImport
            ? null
            : string.IsNullOrWhiteSpace(importedBinding.ExportName)
                ? "default"
                : importedBinding.ExportName;

        return
        [
            .. ResolveExports(context, targetRelativePath, exportName, new(StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal)
        ];
    }

    private static void AddExternalImportEdges(
        TypeScriptPassContext context,
        string importPath,
        TypeScriptImportBinding[] importedBindings,
        string[] callerIds,
        string[] topLevelCallerIds,
        Dictionary<string, HashSet<string>> referencedIdentifiersByCaller,
        Dictionary<string, HashSet<string>> jsxTagNamesByCaller,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeIdentities)
    {
        if (importedBindings.Length == 0)
        {
            var packageId = PackageNodeIdentity.CreateCanonicalId(importPath);
            AddPackageEdgesToCallers(context, topLevelCallerIds, packageId, edgeIdentities);

            return;
        }

        foreach (var importedBinding in importedBindings)
        {
            var matchingCallers = GetReferencingCallers(
                callerIds,
                topLevelCallerIds,
                referencedIdentifiersByCaller,
                importedBinding.LocalName);
            if (importedBinding.IsNamespaceImport)
            {
                foreach (var callerId in matchingCallers)
                {
                    var matchingNamespaceTags = jsxTagNamesByCaller
                        .GetValueOrDefault(callerId, [])
                        .Where(tagName =>
                            tagName.StartsWith($"{importedBinding.LocalName}.", StringComparison.Ordinal))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (matchingNamespaceTags.Length > 0)
                    {
                        foreach (var matchingNamespaceTag in matchingNamespaceTags)
                        {
                            var componentName = matchingNamespaceTag[(importedBinding.LocalName.Length + 1)..];
                            AddPackageEdgesToCallers(
                                context,
                                [callerId],
                                PackageNodeIdentity.CreateCanonicalId(importPath, componentName),
                                edgeIdentities);
                        }

                        continue;
                    }

                    AddPackageEdgesToCallers(
                        context,
                        [callerId],
                        PackageNodeIdentity.CreateCanonicalId(importPath),
                        edgeIdentities);
                }

                continue;
            }

            var targetName = importedBinding.ExportName ?? importedBinding.LocalName;
            AddPackageEdgesToCallers(
                context,
                matchingCallers,
                PackageNodeIdentity.CreateCanonicalId(importPath, targetName),
                edgeIdentities);
        }
    }

    private static void AddImportEdgesToCallers(
        TypeScriptPassContext context,
        IEnumerable<string> callerIds,
        string calleeId,
        string? metadata,
        HashSet<string> knownNodeIds,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeIdentities)
    {
        if (!knownNodeIds.Contains(calleeId) &&
            !PackageNodeIdentity.IsPlaceholderId(calleeId))
        {
            return;
        }

        foreach (var callerId in callerIds)
        {
            if (string.Equals(callerId, calleeId, StringComparison.Ordinal))
            {
                continue;
            }

            if (edgeIdentities.Add((callerId, calleeId, EdgeType.Import)))
            {
                context.Edges.Add(new IndexedDependency(callerId, calleeId, EdgeType.Import, metadata));
            }
        }
    }

    private static void AddPackageEdgesToCallers(
        TypeScriptPassContext context,
        IEnumerable<string> callerIds,
        string packageId,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeIdentities)
    {
        foreach (var callerId in callerIds)
        {
            if (edgeIdentities.Add((callerId, packageId, EdgeType.Import)))
            {
                context.Edges.Add(new IndexedDependency(callerId, packageId, EdgeType.Import, null));
            }
        }
    }

    private static IReadOnlyList<string> GetReferencingCallers(
        string[] callerIds,
        string[] topLevelCallerIds,
        Dictionary<string, HashSet<string>> referencedIdentifiersByCaller,
        string localName)
    {
        var matching = new List<string>();
        foreach (var callerId in callerIds)
        {
            if (referencedIdentifiersByCaller.TryGetValue(callerId, out var identifiers) &&
                identifiers.Contains(localName))
            {
                matching.Add(callerId);
            }
        }

        // Preserve unused or type-only imports on top-level exports without broadcasting them onto class members.
        return matching.Count > 0 ? matching : topLevelCallerIds;
    }

    private static Dictionary<string, HashSet<string>> BuildReferencedIdentifiersByCaller(
        TypeScriptParsedFile parsedFile,
        string[] callerIds,
        TypeScriptPassContext context)
    {
        var indexedDeclarations = callerIds
            .Where(context.DeclarationNodes.ContainsKey)
            .Select(id => context.DeclarationNodes[id])
            .ToHashSet();
        var helperIdentifiers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var statement in parsedFile.RootNode.Statements ?? [])
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (statement is ImportDeclaration or ExportDeclaration)
            {
                continue;
            }

            if (statement is VariableStatement variableStatement)
            {
                foreach (var variable in variableStatement.DeclarationList.Declarations ?? [])
                {
                    if (indexedDeclarations.Contains(variable) ||
                        variable.Name is not Identifier { Text: { } name })
                    {
                        continue;
                    }

                    helperIdentifiers[name] = CollectReferencedIdentifiers(variable, context.CancellationToken);
                }

                continue;
            }

            if (indexedDeclarations.Contains(statement))
            {
                continue;
            }

            var helperName = TypeScriptSyntax.GetName((statement as IHasNameProperty)?.Name);
            if (!string.IsNullOrWhiteSpace(helperName))
            {
                helperIdentifiers[helperName] = CollectReferencedIdentifiers(statement, context.CancellationToken);
            }
        }

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var callerId in callerIds)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!context.DeclarationNodes.TryGetValue(callerId, out var declarationNode))
            {
                continue;
            }

            var identifiers = CollectReferencedIdentifiers(declarationNode, context.CancellationToken);
            if (helperIdentifiers.Count > 0)
            {
                ExpandThroughLocalHelpers(identifiers, helperIdentifiers);
            }

            result[callerId] = identifiers;
        }

        return result;
    }

    private static void ExpandThroughLocalHelpers(
        HashSet<string> identifiers,
        Dictionary<string, HashSet<string>> helperIdentifiers)
    {
        var pending = new Queue<string>(identifiers.Where(helperIdentifiers.ContainsKey));
        var visitedHelpers = new HashSet<string>(pending, StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var helperName = pending.Dequeue();
            foreach (var referenced in helperIdentifiers[helperName])
            {
                identifiers.Add(referenced);
                if (helperIdentifiers.ContainsKey(referenced) && visitedHelpers.Add(referenced))
                {
                    pending.Enqueue(referenced);
                }
            }
        }
    }

    private static HashSet<string> CollectReferencedIdentifiers(INode root, CancellationToken ct)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var ignoredIdentifiers = new HashSet<INode>();

        foreach (var node in root.DescendantsAndSelf(ct))
        {
            switch (node)
            {
                case PropertyAccessExpression { Name: { } propertyName }:
                    ignoredIdentifiers.Add(propertyName);
                    break;
                case PropertyAssignment { Name: Identifier propertyKey }:
                    ignoredIdentifiers.Add(propertyKey);
                    break;
                case ImportDeclaration:
                    continue;
                case Identifier { Text: { } text } identifier when !string.IsNullOrWhiteSpace(text):
                    if (!ignoredIdentifiers.Contains(identifier))
                    {
                        identifiers.Add(text);
                    }

                    break;
            }
        }

        return identifiers;
    }

    private static Dictionary<string, HashSet<string>> BuildJsxTagNamesByCaller(
        TypeScriptParsedFile parsedFile,
        string[] callerIds,
        TypeScriptPassContext context)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var callerId in callerIds)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!context.DeclarationNodes.TryGetValue(callerId, out var declarationNode))
            {
                continue;
            }

            var tags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in declarationNode.DescendantsAndSelf(context.CancellationToken))
            {
                if (node is not IJsxOpeningLikeElement || node is not IJsxHasTagName { TagName: { } tag })
                {
                    continue;
                }

                var tagName = parsedFile.SyntaxTree.GetText(tag).Trim();
                if (!string.IsNullOrWhiteSpace(tagName))
                {
                    tags.Add(tagName);
                }
            }

            if (tags.Count > 0)
            {
                result[callerId] = tags;
            }
        }

        return result;
    }

    private IEnumerable<string> ResolveExports(
        TypeScriptPassContext context,
        string path,
        string? name,
        HashSet<string> visiting)
    {
        if (name is null)
        {
            foreach (var exportedName in GetExportNames(
                context,
                path,
                new(StringComparer.Ordinal))
                .Distinct(StringComparer.Ordinal))
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

            if (!context.FilesByPath.TryGetValue(path, out var source))
            {
                yield break;
            }

            var reExports = context.ReExports
                .Where(binding => binding.FilePath == path)
                .ToArray();
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

                var resolved = tsConfigResolver.ResolveImport(
                    binding.Source,
                    source.DiscoveredFile.AbsolutePath,
                    context.CancellationToken);
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

    private IEnumerable<string> GetExportNames(
        TypeScriptPassContext context,
        string path,
        HashSet<string> visiting)
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

            if (!context.FilesByPath.TryGetValue(path, out var source))
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

                var resolved = tsConfigResolver.ResolveImport(
                    binding.Source,
                    source.DiscoveredFile.AbsolutePath,
                    context.CancellationToken);
                if (resolved is null)
                {
                    continue;
                }

                foreach (var name in GetExportNames(
                    context,
                    repositoryWorkspace.ToRepositoryRelativePath(resolved),
                    visiting))
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

    private static bool IsRelativeImport(string importPath)
        => importPath.StartsWith("./", StringComparison.Ordinal) ||
           importPath.StartsWith("../", StringComparison.Ordinal);
}
