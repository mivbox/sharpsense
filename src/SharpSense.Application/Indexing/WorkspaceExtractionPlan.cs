using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.Models;

namespace SharpSense.Application.Indexing;

internal sealed record WorkspaceExtractionStep(
    WorkspaceSource Source,
    ILanguageExtractor Extractor,
    ExtractionContext Context);

internal static class WorkspaceExtractionPlan
{
    public static IReadOnlyList<WorkspaceExtractionStep> Create(
        IReadOnlyList<WorkspaceSource> sources,
        IEnumerable<ILanguageExtractor> extractors,
        IIndexingWorkspacePaths paths,
        ExtractionContext context)
    {
        var registeredExtractors = extractors.ToArray();
        var steps = new List<WorkspaceExtractionStep>();
        var seenSources = new HashSet<(WorkspaceSourceKind Kind, string Path)>();

        foreach (var source in sources.Where(static source => source.Kind != WorkspaceSourceKind.Markdown))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(source.Path);
            var targetPath = paths.GetRequiredTargetPath(source.Path);
            var relativePath = paths.ToRepositoryRelativePath(targetPath);
            var pathKey = OperatingSystem.IsWindows() ? relativePath.ToUpperInvariant() : relativePath;
            if (!seenSources.Add((source.Kind, pathKey)))
            {
                continue;
            }

            steps.Add(new WorkspaceExtractionStep(
                source,
                GetExtractor(registeredExtractors, source.Kind),
                context with { TargetPath = targetPath }));
        }

        var markdownPatterns = sources
            .Where(static source => source.Kind == WorkspaceSourceKind.Markdown)
            .Select(static source => source.Path)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (markdownPatterns.Length > 0)
        {
            // All documentation selections share a repository-root interpretation and one
            // extraction pass, so cross-selection links are committed in the same snapshot.
            steps.Add(new WorkspaceExtractionStep(
                new WorkspaceSource(WorkspaceSourceKind.Markdown, "."),
                GetExtractor(registeredExtractors, WorkspaceSourceKind.Markdown),
                context with { TargetPath = paths.RootPath, IncludePatterns = markdownPatterns }));
        }

        return steps;
    }

    public static ExtractedNodes SelectEmittedProjects(
        WorkspaceExtractionStep step,
        ExtractedNodes extractedNodes,
        IIndexingWorkspacePaths paths)
    {
        if (step.Source.Kind != WorkspaceSourceKind.CSharp ||
            !Path.GetExtension(step.Context.TargetPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return extractedNodes;
        }

        // Project references remain loaded for Roslyn's semantic model. Only explicitly
        // selected projects contribute declarations; another selection may supply callees.
        var selectedPath = paths.ToRepositoryRelativePath(step.Context.TargetPath);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var projects = extractedNodes.Projects
            .Where(project => paths.ToRepositoryRelativePath(project.RelativeFilePath).Equals(selectedPath, comparison))
            .ToArray();
        var projectIds = projects.Select(static project => project.Id).ToHashSet(StringComparer.Ordinal);
        var codeNodes = extractedNodes.CodeNodes
            .Where(node => node.ProjectId is not null && projectIds.Contains(node.ProjectId))
            .ToArray();
        var callerIds = projectIds.Concat(codeNodes.Select(static node => node.CanonicalId)).ToHashSet(StringComparer.Ordinal);

        return extractedNodes with
        {
            Projects = projects,
            CodeNodes = codeNodes,
            Edges = [.. extractedNodes.Edges.Where(edge => callerIds.Contains(edge.CallerId))]
        };
    }

    private static ILanguageExtractor GetExtractor(
        IReadOnlyList<ILanguageExtractor> extractors,
        WorkspaceSourceKind kind)
        => extractors.SingleOrDefault(extractor => extractor.SourceKind == kind)
           ?? throw new InvalidOperationException($"No extractor is registered for workspace source kind '{kind}'.");
}
