using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using System.Text;

namespace SharpSense.Infrastructure.Indexing.Markdown;

public sealed class MarkdownIndexer
{
    private const string DocumentRootName = "Document Root";
    private const string DocumentRootSlug = "document-root";
    private static readonly string[] _externalLinkPrefixes = ["mailto:", "tel:", "data:", "file:"];
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();
    private static readonly string[] _markdownExtensions = [".md", ".markdown", ".mdown", ".mkd"];

    public MarkdownIndexResult Index(
        string rawText,
        string relativeFilePath)
    {
        ArgumentNullException.ThrowIfNull(rawText);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFilePath);

        if (string.IsNullOrWhiteSpace(rawText))
        {
            return CreateDocumentRootOnlyResult(relativeFilePath);
        }

        var markdownDocument = Markdig.Markdown.Parse(rawText, _pipeline);
        if (markdownDocument.Count == 0)
        {
            return CreateDocumentRootOnlyResult(relativeFilePath);
        }

        var lineMap = new LineMap(rawText);
        var chunkBuilders = new List<MarkdownChunkBuilder>();
        var assignedSlugs = new HashSet<string>(StringComparer.Ordinal) { DocumentRootSlug };
        var currentChunk = MarkdownChunkBuilder.CreateDocumentRoot();
        var activeHeadings = new MarkdownChunkBuilder?[7];
        activeHeadings[0] = currentChunk;

        foreach (var block in markdownDocument)
        {
            if (!TryGetBlockSpan(block, rawText.Length, out var spanStart, out var spanEnd))
            {
                continue;
            }

            var blockStartLine = lineMap.GetLineNumber(spanStart);
            var blockEndLine = lineMap.GetLineNumber(spanEnd);
            var blockText = ExtractRawText(rawText, spanStart, spanEnd);

            if (block is HeadingBlock headingBlock)
            {
                chunkBuilders.Add(currentChunk);
                var level = Math.Clamp(headingBlock.Level, 1, 6);
                MarkdownChunkBuilder? parentChunk = null;

                for (var index = level - 1; index >= 0; index--)
                {
                    if (activeHeadings[index] is null)
                    {
                        continue;
                    }

                    parentChunk = activeHeadings[index];
                    break;
                }

                var headingName = ExtractHeadingName(headingBlock, blockText);
                var slug = CreateUniqueSlug(headingName, blockStartLine, assignedSlugs);
                currentChunk = MarkdownChunkBuilder.CreateChunk(
                    headingName,
                    slug,
                    parentChunk?.Slug,
                    spanStart,
                    spanEnd,
                    blockStartLine,
                    blockEndLine);
                currentChunk.AddLinkTargets(ExtractLinkTargets(block, blockText, relativeFilePath));
                activeHeadings[level] = currentChunk;

                for (var index = level + 1; index <= 6; index++)
                {
                    activeHeadings[index] = null;
                }

                continue;
            }

            currentChunk.AbsorbBlock(spanStart, spanEnd, blockStartLine, blockEndLine);
            currentChunk.AddLinkTargets(ExtractLinkTargets(block, blockText, relativeFilePath));
        }

        chunkBuilders.Add(currentChunk);

        var emittedChunks = chunkBuilders
            .Select(chunk => (Chunk: chunk, Node: ToCodeNode(chunk, rawText, relativeFilePath)))
            .Where(static candidate => candidate.Node.FullyQualifiedName.EndsWith($"#{DocumentRootSlug}", StringComparison.Ordinal) ||
                                       !string.IsNullOrWhiteSpace(candidate.Node.Summary))
            .ToArray();
        var codeNodes = emittedChunks
            .Select(static candidate => candidate.Node)
            .ToArray();
        var edges = BuildDependencyEdges(
            emittedChunks.Select(static candidate => candidate.Chunk),
            relativeFilePath);

        return new MarkdownIndexResult(codeNodes, edges);
    }

    private static MarkdownIndexResult CreateDocumentRootOnlyResult(string relativeFilePath)
        => new([ToCodeNode(MarkdownChunkBuilder.CreateDocumentRoot(), string.Empty, relativeFilePath)], []);

    private static string CreateUniqueSlug(
        string headingName,
        int startLine,
        ISet<string> assignedSlugs)
    {
        var baseSlug = Slugify(headingName);
        if (assignedSlugs.Add(baseSlug))
        {
            return baseSlug;
        }

        var slugWithLine = $"{baseSlug}-l{startLine}";
        if (assignedSlugs.Add(slugWithLine))
        {
            return slugWithLine;
        }

        var duplicateIndex = 2;
        var candidateSlug = $"{slugWithLine}-{duplicateIndex}";

        while (!assignedSlugs.Add(candidateSlug))
        {
            duplicateIndex++;
            candidateSlug = $"{slugWithLine}-{duplicateIndex}";
        }

        return candidateSlug;
    }

    private static string Slugify(string headingName)
    {
        var builder = new StringBuilder(headingName.Length);
        var previousWasSeparator = false;

        foreach (var character in headingName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator)
            {
                continue;
            }

            builder.Append('-');
            previousWasSeparator = true;
        }

        return builder
            .ToString()
            .Trim('-') is { Length: > 0 } slug ? slug : "section";
    }

    private static string ExtractHeadingName(
        HeadingBlock headingBlock,
        string blockText)
    {
        if (headingBlock.Inline is null)
        {
            return blockText.Trim();
        }

        var builder = new StringBuilder();
        AppendInlineText(builder, headingBlock.Inline);
        return string.IsNullOrWhiteSpace(builder.ToString()) ? blockText.Trim() : builder.ToString().Trim();
    }

    private static void AppendInlineText(
        StringBuilder builder,
        ContainerInline containerInline)
    {
        for (var child = containerInline.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case LiteralInline literalInline:
                    builder.Append(literalInline.Content.ToString());
                    break;
                case CodeInline codeInline:
                    builder.Append(codeInline.Content);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case ContainerInline childContainer:
                    AppendInlineText(builder, childContainer);
                    break;
            }
        }
    }

    private static bool TryGetBlockSpan(
        Block block,
        int textLength,
        out int spanStart,
        out int spanEnd)
    {
        spanStart = -1;
        spanEnd = -1;

        var span = block.Span;
        if (span.Start < 0 || span.End < span.Start || textLength == 0)
        {
            return false;
        }

        spanStart = Math.Clamp(span.Start, 0, textLength - 1);
        spanEnd = Math.Clamp(span.End, spanStart, textLength - 1);
        return true;
    }

    private static string ExtractRawText(
        string rawText,
        int spanStart,
        int spanEnd)
        => rawText.Substring(spanStart, spanEnd - spanStart + 1);

    private static DependencyEdge[] BuildDependencyEdges(
        IEnumerable<MarkdownChunkBuilder> chunks,
        string relativeFilePath)
    {
        var edgeKeys = new HashSet<(string CallerId, string CalleeId, EdgeType EdgeType)>();
        var edges = new List<DependencyEdge>();

        foreach (var chunk in chunks)
        {
            var chunkId = BuildDocumentNodeId(relativeFilePath, chunk.Slug);

            if (chunk.ParentSlug is not null)
            {
                TryAddEdge(
                    BuildDocumentNodeId(relativeFilePath, chunk.ParentSlug),
                    chunkId,
                    EdgeType.DocumentHierarchy,
                    edgeKeys,
                    edges);
            }

            foreach (var calleeId in chunk.LinkTargets)
            {
                TryAddEdge(
                    chunkId,
                    calleeId,
                    EdgeType.DocumentLink,
                    edgeKeys,
                    edges);
            }
        }

        return
        [
            .. edges
                .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                .ThenBy(static edge => edge.EdgeType)
        ];
    }

    private static IEnumerable<string> ExtractLinkTargets(
        Block block,
        string blockText,
        string relativeFilePath)
    {
        foreach (var linkInline in EnumerateLinkInlines(block))
        {
            if (TryNormalizeLinkTarget(linkInline, relativeFilePath, out var targetId))
            {
                yield return targetId;
            }
        }

        foreach (var rawWikiLink in EnumerateWikiLinks(block, blockText))
        {
            if (TryNormalizeWikiLinkTarget(rawWikiLink, relativeFilePath, out var targetId))
            {
                yield return targetId;
            }
        }
    }

    private static IEnumerable<LinkInline> EnumerateLinkInlines(Block block)
    {
        if (block is LeafBlock { Inline: not null } leafBlock)
        {
            foreach (var linkInline in EnumerateLinkInlines(leafBlock.Inline!))
            {
                yield return linkInline;
            }
        }

        if (block is ContainerBlock containerBlock)
        {
            foreach (var childBlock in containerBlock)
            {
                foreach (var linkInline in EnumerateLinkInlines(childBlock))
                {
                    yield return linkInline;
                }
            }
        }
    }

    private static IEnumerable<LinkInline> EnumerateLinkInlines(ContainerInline containerInline)
    {
        for (var child = containerInline.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case LinkInline { IsImage: false } linkInline:
                    yield return linkInline;
                    break;
                case ContainerInline childContainer:
                    foreach (var nestedLink in EnumerateLinkInlines(childContainer))
                    {
                        yield return nestedLink;
                    }

                    break;
            }
        }
    }

    private static IEnumerable<string> EnumerateWikiLinks(
        Block block,
        string blockText)
    {
        if (string.IsNullOrWhiteSpace(blockText))
        {
            yield break;
        }

        var rawText = blockText.ToCharArray();
        var blockStart = block.Span.Start;
        if (blockStart >= 0)
        {
            foreach (var inline in EnumerateIgnoredWikiLinkInlines(block))
            {
                if (inline.Span.Start < 0 || inline.Span.End < inline.Span.Start)
                {
                    continue;
                }

                var relativeStart = Math.Clamp(inline.Span.Start - blockStart, 0, rawText.Length - 1);
                var relativeEnd = Math.Clamp(inline.Span.End - blockStart, relativeStart, rawText.Length - 1);

                for (var index = relativeStart; index <= relativeEnd; index++)
                {
                    rawText[index] = ' ';
                }
            }
        }

        foreach (var rawWikiLink in EnumerateWikiLinks(new string(rawText)))
        {
            yield return rawWikiLink;
        }
    }

    private static IEnumerable<string> EnumerateWikiLinks(string text)
    {
        var currentIndex = 0;
        while (currentIndex < text.Length - 1)
        {
            var startIndex = text.IndexOf("[[", currentIndex, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                yield break;
            }

            var endIndex = text.IndexOf("]]", startIndex + 2, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                yield break;
            }

            var rawWikiLink = text[(startIndex + 2)..endIndex].Trim();
            if (!string.IsNullOrWhiteSpace(rawWikiLink))
            {
                yield return rawWikiLink;
            }

            currentIndex = endIndex + 2;
        }
    }

    private static IEnumerable<Inline> EnumerateIgnoredWikiLinkInlines(Block block)
    {
        if (block is LeafBlock { Inline: not null } leafBlock)
        {
            foreach (var inline in EnumerateIgnoredWikiLinkInlines(leafBlock.Inline!))
            {
                yield return inline;
            }
        }

        if (block is not ContainerBlock containerBlock)
        {
            yield break;
        }

        foreach (var childBlock in containerBlock)
        {
            foreach (var inline in EnumerateIgnoredWikiLinkInlines(childBlock))
            {
                yield return inline;
            }
        }
    }

    private static IEnumerable<Inline> EnumerateIgnoredWikiLinkInlines(ContainerInline containerInline)
    {
        for (var child = containerInline.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case CodeInline codeInline:
                    yield return codeInline;
                    break;
                case LinkInline linkInline:
                    yield return linkInline;
                    break;
                case ContainerInline childContainer:
                    foreach (var inline in EnumerateIgnoredWikiLinkInlines(childContainer))
                    {
                        yield return inline;
                    }

                    break;
            }
        }
    }

    private static bool TryNormalizeWikiLinkTarget(
        string rawWikiLink,
        string relativeFilePath,
        out string targetId)
    {
        targetId = string.Empty;

        if (string.IsNullOrWhiteSpace(rawWikiLink))
        {
            return false;
        }

        var aliasSeparatorIndex = rawWikiLink.IndexOf('|');
        var targetWithFragment = aliasSeparatorIndex >= 0
            ? rawWikiLink[..aliasSeparatorIndex]
            : rawWikiLink;
        if (string.IsNullOrWhiteSpace(targetWithFragment))
        {
            return false;
        }

        var fragmentSeparatorIndex = targetWithFragment.IndexOf('#');
        var rawPath = fragmentSeparatorIndex >= 0
            ? targetWithFragment[..fragmentSeparatorIndex]
            : targetWithFragment;
        var rawFragment = fragmentSeparatorIndex >= 0
            ? targetWithFragment[(fragmentSeparatorIndex + 1)..]
            : string.Empty;

        rawPath = rawPath.Trim();
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            targetId = BuildDocumentNodeId(relativeFilePath, NormalizeFragment(rawFragment));
            return true;
        }

        var normalizedPath = NormalizeWikiLinkPath(relativeFilePath, rawPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        targetId = BuildDocumentNodeId(normalizedPath, NormalizeFragment(rawFragment));
        return true;
    }

    private static bool TryNormalizeLinkTarget(
        LinkInline linkInline,
        string relativeFilePath,
        out string targetId)
    {
        targetId = string.Empty;

        if (linkInline.IsImage || string.IsNullOrWhiteSpace(linkInline.Url))
        {
            return false;
        }

        var rawTarget = linkInline.Url.Trim();
        if (IsExternalLink(rawTarget))
        {
            return false;
        }

        var queryIndex = rawTarget.IndexOf('?');
        var fragmentIndex = rawTarget.IndexOf('#');
        var pathEndIndex = queryIndex >= 0 && (fragmentIndex < 0 || queryIndex < fragmentIndex)
            ? queryIndex
            : fragmentIndex;
        var rawPath = pathEndIndex >= 0 ? rawTarget[..pathEndIndex] : rawTarget;
        var rawFragment = fragmentIndex >= 0 ? rawTarget[(fragmentIndex + 1)..] : string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            targetId = BuildDocumentNodeId(relativeFilePath, NormalizeFragment(rawFragment));
            return true;
        }

        if (!IsMarkdownDocumentPath(rawPath))
        {
            return false;
        }

        var normalizedPath = ResolveRelativePath(relativeFilePath, rawPath);
        targetId = BuildDocumentNodeId(normalizedPath, NormalizeFragment(rawFragment));
        return true;
    }

    private static string NormalizeWikiLinkPath(
        string relativeFilePath,
        string rawPath)
    {
        var normalizedPath = rawPath.Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(Path.GetExtension(normalizedPath)))
        {
            normalizedPath = $"{normalizedPath}.md";
        }

        if (!IsMarkdownDocumentPath(normalizedPath))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(normalizedPath))
        {
            return normalizedPath.TrimStart('/', '\\');
        }

        if (normalizedPath.StartsWith("./", StringComparison.Ordinal) ||
            normalizedPath.StartsWith("../", StringComparison.Ordinal))
        {
            return ResolveRelativePath(relativeFilePath, normalizedPath);
        }

        if (relativeFilePath.StartsWith("docs/wiki/", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveRelativePath("docs/wiki/index.md", normalizedPath);
        }

        return ResolveRelativePath(relativeFilePath, normalizedPath);
    }

    private static void TryAddEdge(
        string callerId,
        string calleeId,
        EdgeType edgeType,
        ISet<(string CallerId, string CalleeId, EdgeType EdgeType)> edgeKeys,
        ICollection<DependencyEdge> edges)
    {
        if (!edgeKeys.Add((callerId, calleeId, edgeType)))
        {
            return;
        }

        edges.Add(new DependencyEdge
        {
            CallerId = callerId,
            CalleeId = calleeId,
            EdgeType = edgeType
        });
    }

    private static bool IsExternalLink(string rawTarget)
    {
        if (rawTarget.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        if (rawTarget.IndexOf("://", StringComparison.Ordinal) > 1)
        {
            return true;
        }

        return _externalLinkPrefixes.Any(prefix => rawTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveRelativePath(
        string currentFilePath,
        string targetPath)
    {
        var normalizedTargetPath = targetPath.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalizedTargetPath))
        {
            return normalizedTargetPath.TrimStart('/', '\\');
        }

        var repositoryRoot = GetSyntheticRepositoryRoot();
        var currentAbsolutePath = Path.Combine(repositoryRoot, currentFilePath.Replace('/', Path.DirectorySeparatorChar));
        var currentDirectoryPath = Path.GetDirectoryName(currentAbsolutePath)
                                   ?? repositoryRoot;
        var resolvedAbsolutePath = Path.GetFullPath(
            normalizedTargetPath.Replace('/', Path.DirectorySeparatorChar),
            currentDirectoryPath);

        return Path.GetRelativePath(repositoryRoot, resolvedAbsolutePath).Replace('\\', '/');
    }

    private static string GetSyntheticRepositoryRoot()
    {
        var rootPath = Path.GetPathRoot(Environment.CurrentDirectory);
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            rootPath = Path.GetFullPath(Path.DirectorySeparatorChar.ToString());
        }

        return Path.Combine(rootPath, "__sharpsense_markdown_root__");
    }

    public static bool IsMarkdownDocumentPath(string targetPath)
        => _markdownExtensions.Contains(Path.GetExtension(targetPath), StringComparer.OrdinalIgnoreCase);

    private static string BuildDocumentNodeId(
        string relativeFilePath,
        string? fragment)
        => $"code:doc:{relativeFilePath}#{(string.IsNullOrWhiteSpace(fragment) ? DocumentRootSlug : fragment)}";

    private static string NormalizeFragment(string rawFragment)
        => string.IsNullOrWhiteSpace(rawFragment)
            ? DocumentRootSlug
            : Slugify(Uri.UnescapeDataString(rawFragment).Trim());

    private static CodeNode ToCodeNode(
        MarkdownChunkBuilder chunk,
        string rawText,
        string relativeFilePath)
    {
        var summary = chunk.StartIndex is null || chunk.EndIndex is null
            ? string.Empty
            : ExtractRawText(rawText, chunk.StartIndex.Value, chunk.EndIndex.Value).Trim();

        return new CodeNode
        {
            CanonicalId = BuildDocumentNodeId(relativeFilePath, chunk.Slug),
            ProjectId = null,
            FullyQualifiedName = $"{relativeFilePath}#{chunk.Slug}",
            DisplayName = BuildDocumentDisplayName(relativeFilePath, chunk.Slug),
            NodeType = NodeType.Document,
            RelativeFilePath = relativeFilePath,
            StartLine = chunk.StartLine,
            EndLine = chunk.EndLine,
            Summary = summary
        };
    }

    private static string BuildDocumentDisplayName(string relativeFilePath, string slug)
    {
        var normalizedPath = relativeFilePath.Replace('\\', '/');
        if (normalizedPath.StartsWith("docs/wiki/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath["docs/wiki/".Length..];
        }
        else if (normalizedPath.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath["docs/".Length..];
        }

        var extension = Path.GetExtension(normalizedPath);
        var pathWithoutExtension = string.IsNullOrEmpty(extension)
            ? normalizedPath
            : normalizedPath[..^extension.Length];

        return string.Equals(slug, DocumentRootSlug, StringComparison.Ordinal)
            ? pathWithoutExtension
            : $"{pathWithoutExtension}#{slug}";
    }

    private sealed class MarkdownChunkBuilder(
        string headingName,
        string slug,
        string? parentSlug,
        int? startIndex,
        int? endIndex,
        int startLine,
        int endLine)
    {
        private readonly HashSet<string> _linkTargets = new(StringComparer.Ordinal);

        public string HeadingName { get; } = headingName;

        public string Slug { get; } = slug;

        public string? ParentSlug { get; } = parentSlug;

        public int? StartIndex { get; private set; } = startIndex;

        public int? EndIndex { get; private set; } = endIndex;

        public int StartLine { get; private set; } = startLine;

        public int EndLine { get; private set; } = endLine;

        public IEnumerable<string> LinkTargets => _linkTargets;

        public static MarkdownChunkBuilder CreateDocumentRoot()
            => new(DocumentRootName, DocumentRootSlug, null, null, null, 1, 1);

        public static MarkdownChunkBuilder CreateChunk(
            string headingName,
            string slug,
            string? parentSlug,
            int startIndex,
            int endIndex,
            int startLine,
            int endLine)
            => new(headingName, slug, parentSlug, startIndex, endIndex, startLine, endLine);

        public void AbsorbBlock(
            int startIndex,
            int endIndex,
            int startLine,
            int endLine)
        {
            if (StartIndex is null)
            {
                StartIndex = startIndex;
                StartLine = startLine;
            }

            EndIndex = endIndex;
            EndLine = endLine;
        }

        public void AddLinkTargets(IEnumerable<string> linkTargets)
        {
            foreach (var linkTarget in linkTargets)
            {
                _linkTargets.Add(linkTarget);
            }
        }
    }

    private sealed class LineMap
    {
        private readonly int[] _lineStarts;

        public LineMap(string rawText)
        {
            var lineStarts = new List<int> { 0 };

            for (var index = 0; index < rawText.Length; index++)
            {
                switch (rawText[index])
                {
                    case '\r' when index + 1 < rawText.Length && rawText[index + 1] == '\n':
                        lineStarts.Add(index + 2);
                        index++;
                        break;
                    case '\r':
                    case '\n':
                        lineStarts.Add(index + 1);
                        break;
                }
            }

            _lineStarts = [.. lineStarts];
        }

        public int GetLineNumber(int characterIndex)
        {
            if (characterIndex <= 0)
            {
                return 1;
            }

            var lookupIndex = Array.BinarySearch(_lineStarts, characterIndex);
            return lookupIndex >= 0 ? lookupIndex + 1 : ~lookupIndex;
        }
    }
}
