namespace SharpSense.TypeScript.Parsing.Models;

public static class NodeExtensions
{
    /// <summary>Enumerates syntax descendants in source order without following parent links.</summary>
    public static IEnumerable<INode> DescendantsAndSelf(
        this INode root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);

        var pending = new Stack<INode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return node;

            var children = new List<INode>();
            node.ForEachChild(child =>
            {
                children.Add(child);

                return null;
            });
            for (var index = children.Count - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }
    }

    public static SourceFile? GetSourceFile(this INode? node)
    {
        while (node != null && node is not SourceFile)
        {
            node = node.Parent;
        }

        return node as SourceFile;
    }

    public static bool HasModifier(this INode node, SyntaxKind kind)
    {
        return node is IHasModifierLike hasModifiersLike &&
            hasModifiersLike.Modifiers != null &&
            hasModifiersLike.Modifiers.Any(x => x.Kind == kind);
    }

    public static IEnumerable<Decorator> GetDecorators(this INode node)
    {
        if (node is not IHasModifierLike hasModifiersLike ||
            hasModifiersLike.Modifiers == null)
        {
            return [];
        }

        return hasModifiersLike.Modifiers.OfType<Decorator>();
    }

    public static string? GetText(this INode node, string? sourceText = null)
    {
        if ((sourceText ??= node.GetSourceFile()?.Text) == null)
        {
            return null;
        }

        if (node.Pos == null || node.End == null)
        {
            return null;
        }

        var pos = Scanner.SkipTrivia(sourceText!, node.Pos) ?? node.Pos ?? 0;
        if (node.End < pos)
        {
            return null;
        }

        return sourceText![pos..node.End.Value];
    }

    public static string? GetTextWithTrivia(this INode node, string? sourceText = null)
    {
        if ((sourceText ??= node.GetSourceFile()?.Text) == null)
        {
            return null;
        }

        if (node.Pos == null || node.End == null || node.End < node.Pos)
        {
            return null;
        }

        return sourceText![(int)node.Pos..(int)node.End];
    }

    public static INode? ForEachChild(this INode node, Func<INode, INode?> visitor, bool recursively = false)
    {
        if (node == null)
        {
            return null;
        }

        if (recursively)
        {
            foreach (var descendant in node.DescendantsAndSelf().Skip(1))
            {
                if (visitor(descendant) is { } match)
                {
                    return match;
                }
            }

            return null;
        }

        INode? result;

        if (node is IHasModifierLike hasModifierLike && hasModifierLike.Modifiers != null)
        {
            foreach (var modifier in hasModifierLike.Modifiers)
            {
                if (modifier == null)
                {
                    continue;
                }

                if ((result = visitor(modifier)) != null)
                {
                    return result;
                }

            }
        }

        if (node is not IGetRestChildren getRestChildren)
        {
            return null;
        }

        var children = getRestChildren.GetRestChildren();
        if (children == null)
        {
            return null;
        }

        foreach (var child in children)
        {
            if (child == null)
            {
                continue;
            }

            if ((result = visitor(child)) != null)
            {
                return result;
            }

        }

        return null;
    }
}
