using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.TypeScript.Parsing.Abstractions;
using SharpSense.TypeScript.Parsing.Models;

namespace SharpSense.Infrastructure.Indexing.TypeScript;

/// <summary>
/// Extracts syntax-level symbol calls, JSX component renders, instantiations, and heritage edges
/// from parsed TypeScript/TSX declarations using local declarations, resolved imports, and
/// lightweight receiver type annotations (including <c>new</c> and <c>useRef&lt;T&gt;</c>).
/// </summary>
internal sealed class TypeScriptSymbolReferencePass : ITypeScriptExtractionPass
{
    public void Execute(TypeScriptPassContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var codeNodesById = context.CodeNodes
            .ToDictionary(static node => node.CanonicalId, StringComparer.Ordinal);
        var membersByOwnerId = BuildMembersByOwnerId(context, codeNodesById);
        var ownerByMemberId = BuildOwnerByMemberId(context, codeNodesById);
        var codeNodesByFile = context.CodeNodes.ToLookup(static node => node.RelativeFilePath, StringComparer.Ordinal);
        var seenEdges = context.Edges
            .Select(static edge => (edge.CallerId, edge.CalleeId, edge.EdgeType))
            .ToHashSet();

        foreach (var parsedFile in context.ParsedFiles)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var filePath = parsedFile.DiscoveredFile.RelativeFilePath;
            var fileCodeNodes = codeNodesByFile[filePath]
                .Where(codeNode => context.DeclarationNodes.ContainsKey(codeNode.CanonicalId))
                .OrderBy(codeNode => context.DeclarationNodes[codeNode.CanonicalId].End - context.DeclarationNodes[codeNode.CanonicalId].Pos)
                .ThenBy(static codeNode => codeNode.StartLine)
                .ToArray();
            if (fileCodeNodes.Length == 0)
            {
                continue;
            }

            var localSymbols = BuildLocalSymbols(filePath, fileCodeNodes, ownerByMemberId, context);
            var localImports = context.LocalImportsByFile.GetValueOrDefault(filePath)
                ?? new Dictionary<string, ResolvedLocalImportBinding>(StringComparer.Ordinal);
            var fieldTypesByClassId = BuildClassFieldTypeBindings(
                fileCodeNodes,
                context,
                localSymbols,
                localImports,
                codeNodesById);
            var variableTypesByCallerId = BuildVariableTypeBindings(
                fileCodeNodes,
                context,
                localSymbols,
                localImports,
                codeNodesById);

            EmitHeritageEdges(
                fileCodeNodes,
                context,
                localSymbols,
                localImports,
                codeNodesById,
                seenEdges);

            foreach (var node in parsedFile.RootNode.DescendantsAndSelf(context.CancellationToken))
            {
                switch (node)
                {
                    case CallExpression call:
                        EmitCallEdge(
                            call,
                            fileCodeNodes,
                            context,
                            localSymbols,
                            localImports,
                            membersByOwnerId,
                            ownerByMemberId,
                            fieldTypesByClassId,
                            variableTypesByCallerId,
                            codeNodesById,
                            seenEdges);
                        break;
                    case NewExpression newExpression:
                        EmitInstantiationEdge(
                            newExpression,
                            fileCodeNodes,
                            context,
                            localSymbols,
                            localImports,
                            membersByOwnerId,
                            codeNodesById,
                            seenEdges);
                        break;
                    case PropertyAccessExpression propertyAccess when IsThisExpression(UnwrapExpression(propertyAccess.Expression)):
                        EmitThisMethodReferenceEdge(
                            propertyAccess,
                            fileCodeNodes,
                            context,
                            membersByOwnerId,
                            ownerByMemberId,
                            codeNodesById,
                            seenEdges);
                        break;
                    case JsxOpeningElement jsxOpening:
                        EmitJsxEdge(
                            jsxOpening,
                            jsxOpening.TagName,
                            fileCodeNodes,
                            context,
                            localSymbols,
                            localImports,
                            codeNodesById,
                            seenEdges);
                        break;
                    case JsxSelfClosingElement jsxSelfClosing:
                        EmitJsxEdge(
                            jsxSelfClosing,
                            jsxSelfClosing.TagName,
                            fileCodeNodes,
                            context,
                            localSymbols,
                            localImports,
                            codeNodesById,
                            seenEdges);
                        break;
                }
            }
        }
    }

    private static void EmitHeritageEdges(
        IndexedCodeNode[] fileCodeNodes,
        TypeScriptPassContext context,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> seenEdges)
    {
        foreach (var codeNode in fileCodeNodes)
        {
            if (!context.DeclarationNodes.TryGetValue(codeNode.CanonicalId, out var declaration))
            {
                continue;
            }

            var clauses = declaration switch
            {
                IClassLikeDeclaration { HeritageClauses: { Count: > 0 } classClauses } => classClauses,
                InterfaceDeclaration { HeritageClauses: { Count: > 0 } interfaceClauses } => interfaceClauses,
                _ => null
            };
            if (clauses is null)
            {
                continue;
            }

            foreach (var clause in clauses)
            {
                foreach (var expressionWithTypeArgs in clause.Types)
                {
                    foreach (var targetId in ResolveExpressionTargets(
                        expressionWithTypeArgs.Expression,
                        context,
                        localSymbols,
                        localImports,
                        codeNodesById))
                    {
                        AddEdge(context, codeNode.CanonicalId, targetId, EdgeType.Implements, seenEdges);
                    }
                }
            }
        }
    }

    private static void EmitInstantiationEdge(
        NewExpression newExpression,
        IndexedCodeNode[] fileCodeNodes,
        TypeScriptPassContext context,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, Dictionary<string, string>> membersByOwnerId,
        Dictionary<string, IndexedCodeNode> codeNodesById,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> seenEdges)
    {
        var caller = FindEnclosingCaller(newExpression, fileCodeNodes, context);
        if (caller is null)
        {
            return;
        }

        foreach (var targetId in ResolveExpressionTargets(
            newExpression.Expression,
            context,
            localSymbols,
            localImports,
            codeNodesById))
        {
            AddEdge(context, caller.CanonicalId, targetId, EdgeType.Instantiates, seenEdges);
            if (TryResolveMember(membersByOwnerId, targetId, "constructor", out var constructorId))
            {
                AddEdge(context, caller.CanonicalId, constructorId, EdgeType.MethodCall, seenEdges);
            }
        }
    }

    private static void EmitThisMethodReferenceEdge(
        PropertyAccessExpression propertyAccess,
        IndexedCodeNode[] fileCodeNodes,
        TypeScriptPassContext context,
        Dictionary<string, Dictionary<string, string>> membersByOwnerId,
        Dictionary<string, string> ownerByMemberId,
        Dictionary<string, IndexedCodeNode> codeNodesById,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> seenEdges)
    {
        if (propertyAccess.Name?.Text is not { Length: > 0 } memberName)
        {
            return;
        }

        var caller = FindEnclosingCaller(propertyAccess, fileCodeNodes, context);
        if (caller is null)
        {
            return;
        }

        var owningClassId = ResolveOwningClassId(caller.CanonicalId, ownerByMemberId, codeNodesById);
        if (owningClassId is null ||
            !TryResolveMember(membersByOwnerId, owningClassId, memberName, out var memberId))
        {
            return;
        }

        if (codeNodesById.TryGetValue(memberId, out var memberNode) &&
            (memberNode.NodeType == NodeType.Method ||
             context.DeclarationNodes.TryGetValue(memberId, out var memberDecl) &&
             memberDecl is PropertyDeclaration { Initializer: ArrowFunction or FunctionExpression }))
        {
            AddEdge(context, caller.CanonicalId, memberId, EdgeType.MethodCall, seenEdges);
        }
    }

    private static void EmitJsxEdge(
        INode jsxNode,
        IJsxTagNameExpression? tagName,
        IndexedCodeNode[] fileCodeNodes,
        TypeScriptPassContext context,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> seenEdges)
    {
        if (tagName is null)
        {
            return;
        }

        if (tagName is Identifier { Text: { Length: > 0 } tagText } && !char.IsUpper(tagText[0]))
        {
            return;
        }

        var caller = FindEnclosingCaller(jsxNode, fileCodeNodes, context);
        if (caller is null)
        {
            return;
        }

        foreach (var targetId in ResolveExpressionTargets(
            tagName,
            context,
            localSymbols,
            localImports,
            codeNodesById))
        {
            AddEdge(context, caller.CanonicalId, targetId, EdgeType.MethodCall, seenEdges);
        }
    }

    private static void EmitCallEdge(
        CallExpression call,
        IndexedCodeNode[] fileCodeNodes,
        TypeScriptPassContext context,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, Dictionary<string, string>> membersByOwnerId,
        Dictionary<string, string> ownerByMemberId,
        Dictionary<string, Dictionary<string, string>> fieldTypesByClassId,
        Dictionary<string, Dictionary<string, string>> variableTypesByCallerId,
        Dictionary<string, IndexedCodeNode> codeNodesById,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> seenEdges)
    {
        var caller = FindEnclosingCaller(call, fileCodeNodes, context);
        if (caller is null)
        {
            return;
        }

        var calleeExpression = UnwrapExpression(call.Expression);
        if (calleeExpression is Identifier { Text: { } directName })
        {
            foreach (var targetId in ResolveIdentifierTargets(directName, localSymbols, localImports, codeNodesById))
            {
                AddEdge(context, caller.CanonicalId, targetId, EdgeType.MethodCall, seenEdges);
            }

            return;
        }

        if (calleeExpression is not PropertyAccessExpression { Name.Text: { } memberName } propertyAccess)
        {
            return;
        }

        var receiver = UnwrapExpression(propertyAccess.Expression);

        // Case 1: this.member(...)
        if (IsThisExpression(receiver))
        {
            var owningClassId = ResolveOwningClassId(caller.CanonicalId, ownerByMemberId, codeNodesById);
            if (owningClassId is not null &&
                TryResolveMember(membersByOwnerId, owningClassId, memberName, out var memberId))
            {
                AddEdge(context, caller.CanonicalId, memberId, EdgeType.MethodCall, seenEdges);
            }

            return;
        }

        // Case 2: this.field.member(...)
        if (receiver is PropertyAccessExpression { Name.Text: { } fieldName } fieldAccess &&
            IsThisExpression(UnwrapExpression(fieldAccess.Expression)))
        {
            var owningClassId = ResolveOwningClassId(caller.CanonicalId, ownerByMemberId, codeNodesById);
            if (owningClassId is not null &&
                fieldTypesByClassId.TryGetValue(owningClassId, out var fieldTypes) &&
                fieldTypes.TryGetValue(fieldName, out var fieldTypeId) &&
                TryResolveMember(membersByOwnerId, fieldTypeId, memberName, out var fieldMemberId))
            {
                AddEdge(context, caller.CanonicalId, fieldMemberId, EdgeType.MethodCall, seenEdges);
            }

            return;
        }

        // Case 3: refVar.current.member(...) / refVar.current?.member(...)
        if (receiver is PropertyAccessExpression { Name.Text: "current" } currentAccess &&
            UnwrapExpression(currentAccess.Expression) is Identifier { Text: { } refName } &&
            variableTypesByCallerId.TryGetValue(caller.CanonicalId, out var callerVarsForRef) &&
            callerVarsForRef.TryGetValue(refName, out var refTypeId) &&
            TryResolveMember(membersByOwnerId, refTypeId, memberName, out var refMemberId))
        {
            AddEdge(context, caller.CanonicalId, refMemberId, EdgeType.MethodCall, seenEdges);

            return;
        }

        if (receiver is not Identifier { Text: { } receiverName })
        {
            return;
        }

        // Case 4: namespaceImport.exportedFn(...)
        if (localImports.TryGetValue(receiverName, out var importBinding) &&
            importBinding.IsNamespaceImport &&
            context.ExportsByFile.TryGetValue(importBinding.TargetRelativePath, out var exports) &&
            exports.TryGetValue(memberName, out var exportedId) &&
            codeNodesById.ContainsKey(exportedId))
        {
            AddEdge(context, caller.CanonicalId, exportedId, EdgeType.MethodCall, seenEdges);

            return;
        }

        // Case 5: typedVar.member(...) (from `const x = new Foo()`, `x: Foo`, or `const x = ref.current`)
        if (variableTypesByCallerId.TryGetValue(caller.CanonicalId, out var callerVars) &&
            callerVars.TryGetValue(receiverName, out var receiverTypeId) &&
            TryResolveMember(membersByOwnerId, receiverTypeId, memberName, out var typedMemberId))
        {
            AddEdge(context, caller.CanonicalId, typedMemberId, EdgeType.MethodCall, seenEdges);

            return;
        }

        // Case 6: ClassName.staticMethod(...)
        foreach (var candidateOwnerId in ResolveIdentifierTargets(receiverName, localSymbols, localImports, codeNodesById))
        {
            if (TryResolveMember(membersByOwnerId, candidateOwnerId, memberName, out var staticMemberId))
            {
                AddEdge(context, caller.CanonicalId, staticMemberId, EdgeType.MethodCall, seenEdges);
            }
        }
    }

    private static Dictionary<string, Dictionary<string, string>> BuildClassFieldTypeBindings(
        IndexedCodeNode[] fileCodeNodes,
        TypeScriptPassContext context,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        var bindings = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var codeNode in fileCodeNodes)
        {
            if (codeNode.NodeType != NodeType.Class ||
                !context.DeclarationNodes.TryGetValue(codeNode.CanonicalId, out var declaration) ||
                declaration is not IClassLikeDeclaration { Members: { } members })
            {
                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in members.OfType<PropertyDeclaration>())
            {
                var fieldName = TypeScriptSyntax.GetName(property.Name);
                if (string.IsNullOrWhiteSpace(fieldName))
                {
                    continue;
                }

                var resolvedType = ResolveTypeIdFromAnnotationOrInitializer(
                    property.Type,
                    property.Initializer,
                    localSymbols,
                    localImports,
                    codeNodesById,
                    existingBindings: null);
                if (resolvedType is not null)
                {
                    fields[fieldName] = resolvedType;
                }
            }

            if (fields.Count > 0)
            {
                bindings[codeNode.CanonicalId] = fields;
            }
        }

        return bindings;
    }

    private static Dictionary<string, Dictionary<string, string>> BuildVariableTypeBindings(
        IndexedCodeNode[] fileCodeNodes,
        TypeScriptPassContext context,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var codeNode in fileCodeNodes)
        {
            if (!context.DeclarationNodes.TryGetValue(codeNode.CanonicalId, out var declaration))
            {
                continue;
            }

            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var descendant in declaration.DescendantsAndSelf(context.CancellationToken))
            {
                switch (descendant)
                {
                    case ParameterDeclaration { Name: Identifier { Text: { } paramName }, Type: { } paramType }:
                        {
                            var resolved = ResolveTypeIdFromTypeNode(paramType, localSymbols, localImports, codeNodesById);
                            if (resolved is not null)
                            {
                                vars.TryAdd(paramName, resolved);
                            }

                            break;
                        }
                    case VariableDeclaration { Name: Identifier { Text: { } varName } } variable:
                        {
                            var resolved = ResolveTypeIdFromAnnotationOrInitializer(
                                variable.Type,
                                variable.Initializer,
                                localSymbols,
                                localImports,
                                codeNodesById,
                                vars);
                            if (resolved is not null)
                            {
                                vars[varName] = resolved;
                            }

                            break;
                        }
                }
            }

            if (vars.Count > 0)
            {
                result[codeNode.CanonicalId] = vars;
            }
        }

        return result;
    }

    private static string? ResolveTypeIdFromAnnotationOrInitializer(
        ITypeNode? typeAnnotation,
        IExpression? initializer,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById,
        Dictionary<string, string>? existingBindings)
    {
        if (ResolveTypeIdFromTypeNode(typeAnnotation, localSymbols, localImports, codeNodesById) is { } annotated)
        {
            return annotated;
        }

        var unwrapped = UnwrapExpression(initializer);
        return unwrapped switch
        {
            NewExpression { Expression: Identifier { Text: { } className } }
                => ResolveSingleTypeTarget(className, localSymbols, localImports, codeNodesById),
            CallExpression { TypeArguments: { Count: 1 } typeArgs } call when IsUseRefCall(call)
                => ResolveTypeIdFromTypeNode(typeArgs[0], localSymbols, localImports, codeNodesById),
            PropertyAccessExpression { Name.Text: "current" } currentAccess
                when existingBindings is not null &&
                     UnwrapExpression(currentAccess.Expression) is Identifier { Text: { } refName } &&
                     existingBindings.TryGetValue(refName, out var refTypeId)
                => refTypeId,
            _ => null
        };
    }

    private static bool IsUseRefCall(CallExpression call)
        => UnwrapExpression(call.Expression) switch
        {
            Identifier { Text: "useRef" } => true,
            PropertyAccessExpression { Name.Text: "useRef" } => true,
            _ => false
        };

    private static string? ResolveTypeIdFromTypeNode(
        ITypeNode? typeNode,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        switch (typeNode)
        {
            case TypeReferenceNode { TypeName: Identifier { Text: { } typeName } }:
                return ResolveSingleTypeTarget(typeName, localSymbols, localImports, codeNodesById);
            case ParenthesizedTypeNode { Type: { } inner }:
                return ResolveTypeIdFromTypeNode(inner, localSymbols, localImports, codeNodesById);
            case UnionTypeNode { Types: { Count: > 0 } members }:
                foreach (var member in members)
                {
                    if (ResolveTypeIdFromTypeNode(member, localSymbols, localImports, codeNodesById) is { } match)
                    {
                        return match;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private static string? ResolveSingleTypeTarget(
        string name,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        foreach (var candidateId in ResolveIdentifierTargets(name, localSymbols, localImports, codeNodesById))
        {
            if (codeNodesById.TryGetValue(candidateId, out var node) &&
                node.NodeType is NodeType.Class or NodeType.Interface)
            {
                return candidateId;
            }
        }

        return null;
    }

    private static IEnumerable<string> ResolveExpressionTargets(
        INode expression,
        TypeScriptPassContext context,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        var unwrapped = UnwrapExpression(expression);
        if (unwrapped is Identifier { Text: { } name })
        {
            return ResolveIdentifierTargets(name, localSymbols, localImports, codeNodesById);
        }

        if (unwrapped is PropertyAccessExpression { Name.Text: { } exportName } access &&
            UnwrapExpression(access.Expression) is Identifier { Text: { } ns } &&
            localImports.TryGetValue(ns, out var binding) &&
            binding.IsNamespaceImport &&
            context.ExportsByFile.TryGetValue(binding.TargetRelativePath, out var exports) &&
            exports.TryGetValue(exportName, out var exportedId) &&
            codeNodesById.ContainsKey(exportedId))
        {
            return [exportedId];
        }

        return [];
    }

    private static IEnumerable<string> ResolveIdentifierTargets(
        string name,
        Dictionary<string, string> localSymbols,
        Dictionary<string, ResolvedLocalImportBinding> localImports,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        if (localSymbols.TryGetValue(name, out var localId) && codeNodesById.ContainsKey(localId))
        {
            return [localId];
        }

        if (localImports.TryGetValue(name, out var binding) && !binding.IsNamespaceImport)
        {
            return binding.TargetNodeIds.Where(codeNodesById.ContainsKey);
        }

        return [];
    }

    private static Dictionary<string, string> BuildLocalSymbols(
        string filePath,
        IndexedCodeNode[] fileCodeNodes,
        Dictionary<string, string> ownerByMemberId,
        TypeScriptPassContext context)
    {
        var symbols = new Dictionary<string, string>(StringComparer.Ordinal);
        if (context.ExportsByFile.TryGetValue(filePath, out var exports))
        {
            foreach (var (exportName, canonicalId) in exports)
            {
                if (exportName != "default")
                {
                    symbols.TryAdd(exportName, canonicalId);
                }
            }
        }

        foreach (var node in fileCodeNodes)
        {
            if (ownerByMemberId.ContainsKey(node.CanonicalId))
            {
                continue;
            }

            var displayName = node.DisplayName.EndsWith("()", StringComparison.Ordinal)
                ? node.DisplayName[..^2]
                : node.DisplayName;
            if (!string.IsNullOrWhiteSpace(displayName) && displayName != "default")
            {
                symbols.TryAdd(displayName, node.CanonicalId);
            }
        }

        return symbols;
    }

    private static Dictionary<string, Dictionary<string, string>> BuildMembersByOwnerId(
        TypeScriptPassContext context,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        var map = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (parentId, memberId) in context.ParentEdges)
        {
            if (!codeNodesById.ContainsKey(parentId) ||
                !codeNodesById.TryGetValue(memberId, out var memberNode))
            {
                continue;
            }

            var shortName = ExtractMemberShortName(memberNode.DisplayName);
            if (string.IsNullOrWhiteSpace(shortName))
            {
                continue;
            }

            if (!map.TryGetValue(parentId, out var members))
            {
                members = new Dictionary<string, string>(StringComparer.Ordinal);
                map[parentId] = members;
            }

            if (!members.TryGetValue(shortName, out var existingId) ||
                codeNodesById[existingId].NodeType != NodeType.Method && memberNode.NodeType == NodeType.Method)
            {
                members[shortName] = memberId;
            }
        }

        return map;
    }

    private static Dictionary<string, string> BuildOwnerByMemberId(
        TypeScriptPassContext context,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (parentId, memberId) in context.ParentEdges)
        {
            if (codeNodesById.ContainsKey(parentId) && codeNodesById.ContainsKey(memberId))
            {
                map.TryAdd(memberId, parentId);
            }
        }

        return map;
    }

    private static string ExtractMemberShortName(string displayName)
    {
        var trimmed = displayName;
        var bracketIndex = trimmed.IndexOf(" [", StringComparison.Ordinal);
        if (bracketIndex >= 0)
        {
            trimmed = trimmed[..bracketIndex];
        }

        if (trimmed.EndsWith("()", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^2];
        }

        var dotIndex = trimmed.LastIndexOf('.');

        return dotIndex >= 0 ? trimmed[(dotIndex + 1)..] : trimmed;
    }

    private static string? ResolveOwningClassId(
        string callerId,
        Dictionary<string, string> ownerByMemberId,
        Dictionary<string, IndexedCodeNode> codeNodesById)
    {
        if (ownerByMemberId.TryGetValue(callerId, out var ownerId))
        {
            return ownerId;
        }

        return codeNodesById.TryGetValue(callerId, out var node) && node.NodeType == NodeType.Class
            ? callerId
            : null;
    }

    private static bool TryResolveMember(
        Dictionary<string, Dictionary<string, string>> membersByOwnerId,
        string ownerId,
        string memberName,
        out string memberId)
    {
        if (membersByOwnerId.TryGetValue(ownerId, out var members) &&
            members.TryGetValue(memberName, out var resolved))
        {
            memberId = resolved;

            return true;
        }

        memberId = string.Empty;

        return false;
    }

    private static IndexedCodeNode? FindEnclosingCaller(
        INode target,
        IndexedCodeNode[] fileCodeNodes,
        TypeScriptPassContext context)
        => fileCodeNodes.FirstOrDefault(codeNode =>
            context.DeclarationNodes[codeNode.CanonicalId].Pos <= target.Pos &&
            context.DeclarationNodes[codeNode.CanonicalId].End >= target.End);

    private static bool IsThisExpression(INode? node)
        => node is ThisExpression or { Kind: SyntaxKind.ThisKeyword };

    private static INode? UnwrapExpression(INode? node)
    {
        while (node is ParenthesizedExpression or NonNullExpression or AsExpression)
        {
            node = node switch
            {
                ParenthesizedExpression parenthesized => parenthesized.Expression,
                NonNullExpression nonNull => nonNull.Expression,
                AsExpression asExpression => asExpression.Expression,
                _ => node
            };
        }

        return node;
    }

    private static void AddEdge(
        TypeScriptPassContext context,
        string callerId,
        string calleeId,
        EdgeType edgeType,
        HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)> seenEdges)
    {
        if (string.Equals(callerId, calleeId, StringComparison.Ordinal))
        {
            return;
        }

        if (seenEdges.Add((callerId, calleeId, edgeType)))
        {
            context.Edges.Add(new IndexedDependency(callerId, calleeId, edgeType));
        }
    }
}
