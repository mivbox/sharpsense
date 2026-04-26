using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Features.Indexing.IndexTarget;
using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Application.Features.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class KnowledgeGraphIndexing(
    IEnumerable<ILanguageExtractor> extractors,
    IEmbeddingGenerator embeddingsService,
    SharpSenseDbContext context,
    IRepositoryWorkspace workspace,
    IOptions<SharpSenseCliOptions> cliOptions)
    : IKnowledgeGraphIndexing
{
    private readonly SharpSenseCliOptions _cliOptions = cliOptions?.Value ?? throw new ArgumentNullException(nameof(cliOptions));

    public async Task Index(IndexTargetCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var absoluteTargetPath = GetRequiredTargetPath();

        using var trace = SharpSenseTraceSpan.Start("index.target");
        trace.AddTag("target.path", absoluteTargetPath);
        trace.AddTag("repository.root", workspace.RootPath);
        trace.AddTag("index.include_embeddings", !_cliOptions.SkipEmbeddings);

        try
        {
            using var extractActivity = SharpSenseTraceSpan.Start("index.extract");
            var extractionContext = new ExtractionContext(absoluteTargetPath, command.Progress);
            var aggregatedProjects = new List<ProjectNode>();
            var aggregatedCodeNodes = new List<CodeNode>();
            var aggregatedEdges = new List<DependencyEdge>();
            var aggregatedDiagnostics = new List<string>();

            foreach (var extractor in extractors)
            {
                var extractedNodes = await extractor.Extract(extractionContext, ct);

                aggregatedProjects.AddRange(extractedNodes.Projects);
                aggregatedCodeNodes.AddRange(extractedNodes.CodeNodes);
                aggregatedEdges.AddRange(extractedNodes.Edges);
                aggregatedDiagnostics.AddRange(extractedNodes.Diagnostics);

                extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.project.count", extractedNodes.Projects.Count);
                extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.code_node.count", extractedNodes.CodeNodes.Count);
                extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.dependency.count", extractedNodes.Edges.Count);
                extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.diagnostic.count", extractedNodes.Diagnostics.Count);
            }

            var codeNodes = aggregatedCodeNodes
                .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
                .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
                .ToArray();
            var projectCount = aggregatedProjects.Count;
            var documentNodeCount = codeNodes.Count(static codeNode => codeNode.NodeType == NodeType.Document);

            extractActivity.AddTag("index.project.count", aggregatedProjects.Count);
            extractActivity.AddTag("index.code_node.count", codeNodes.Length);
            extractActivity.AddTag("index.document_node.count", documentNodeCount);
            extractActivity.AddTag("index.dependency.count", aggregatedEdges.Count);
            extractActivity.AddTag("index.diagnostic.count", aggregatedDiagnostics.Count);

            if (!_cliOptions.SkipEmbeddings && codeNodes.Length > 0)
            {
                command.Progress?.Report(new IndexingProgress("Embedding phase...", projectCount, projectCount));
                await PopulateEmbeddings(codeNodes, command.EmbeddingProgress, ct);
            }

            NormalizePersistedPaths(aggregatedProjects, codeNodes);
            command.Progress?.Report(new IndexingProgress("Persisting index...", projectCount, projectCount));

            using (var persistActivity = SharpSenseTraceSpan.Start("index.persist"))
            {
                await using var transaction = await context.Database.BeginTransactionAsync(ct);
                await AssignPersistedCodeNodeIds(codeNodes, ct);

                await context.DependencyEdges.ExecuteDeleteAsync(ct);
                await context.CodeNodes.ExecuteDeleteAsync(ct);
                await context.ProjectNodes.ExecuteDeleteAsync(ct);
                context.ChangeTracker.Clear();

                await context.ProjectNodes.AddRangeAsync(aggregatedProjects, ct);
                await context.CodeNodes.AddRangeAsync(codeNodes, ct);
                await context.DependencyEdges.AddRangeAsync(aggregatedEdges, ct);
                await context.SaveChangesAsync(ct);

                await context.Database.ExecuteSqlRawAsync(
                    """
                    DELETE FROM CodeNodeSearch;
                    INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath)
                    SELECT Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath
                    FROM CodeNodes;
                    """,
                    ct);

                await transaction.CommitAsync(ct);

                persistActivity.AddTag("index.project.count", aggregatedProjects.Count);
                persistActivity.AddTag("index.code_node.count", codeNodes.Length);
                persistActivity.AddTag("index.document_node.count", documentNodeCount);
                persistActivity.AddTag("index.dependency.count", aggregatedEdges.Count);
                persistActivity.AddTag("index.diagnostic.count", aggregatedDiagnostics.Count);
            }

            trace.AddTag("index.project.count", aggregatedProjects.Count);
            trace.AddTag("index.code_node.count", codeNodes.Length);
            trace.AddTag("index.document_node.count", documentNodeCount);
            trace.AddTag("index.dependency.count", aggregatedEdges.Count);
            trace.AddTag("index.diagnostic.count", aggregatedDiagnostics.Count);

        }
        catch (Exception e)
        {
            trace.RecordExceptionAndErrorStatus(e);
            throw;
        }
    }

    public async Task UpdateIncremental(UpdateWorkspaceFilesCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.ChangedFiles);

        var absoluteTargetPath = GetRequiredTargetPath();

        using var trace = SharpSenseTraceSpan.Start("index.target.incremental");
        trace.AddTag("target.path", absoluteTargetPath);
        trace.AddTag("repository.root", workspace.RootPath);
        trace.AddTag("index.change.count", command.ChangedFiles.Count);

        try
        {
            using var extractActivity = SharpSenseTraceSpan.Start("index.extract.incremental");
            var extractionContext = new IncrementalExtractionContext(absoluteTargetPath, command.ChangedFiles, command.Progress);
            var aggregatedProjects = new List<ProjectNode>();
            var aggregatedCodeNodes = new List<CodeNode>();
            var aggregatedEdges = new List<DependencyEdge>();
            var aggregatedDiagnostics = new List<string>();

            foreach (var extractor in extractors)
            {
                var extractedNodes = await extractor.ExtractIncremental(extractionContext, ct);

                aggregatedProjects.AddRange(extractedNodes.Projects);
                aggregatedCodeNodes.AddRange(extractedNodes.CodeNodes);
                aggregatedEdges.AddRange(extractedNodes.Edges);
                aggregatedDiagnostics.AddRange(extractedNodes.Diagnostics);

                extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.project.count", extractedNodes.Projects.Count);
                extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.code_node.count", extractedNodes.CodeNodes.Count);
                extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.dependency.count", extractedNodes.Edges.Count);
                extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.diagnostic.count", extractedNodes.Diagnostics.Count);
            }

            var codeNodes = aggregatedCodeNodes
                .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
                .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
                .ToArray();
            var changedFilePaths = GetAffectedRelativePaths(command.ChangedFiles);

            NormalizePersistedPaths([], codeNodes);
            command.Progress?.Report(new IndexingProgress("Persisting incremental index...", changedFilePaths.Length, changedFilePaths.Length));

            using var persistActivity = SharpSenseTraceSpan.Start("index.persist.incremental");
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            var changedNodeIdentities = changedFilePaths.Length == 0
                ? []
                : await context.CodeNodes
                    .AsNoTracking()
                    .Where(codeNode => changedFilePaths.Contains(codeNode.RelativeFilePath))
                    .Select(static codeNode => new PersistedCodeNodeIdentity(codeNode.Id, codeNode.CanonicalId))
                    .ToArrayAsync(ct);
            AssignPersistedCodeNodeIds(codeNodes, changedNodeIdentities);
            var changedCanonicalIds = changedNodeIdentities
                .Select(static codeNode => codeNode.CanonicalId)
                .ToArray();
            var persistedCanonicalIds = codeNodes
                .Select(static codeNode => codeNode.CanonicalId)
                .ToHashSet(StringComparer.Ordinal);
            var removedCanonicalIds = changedCanonicalIds
                .Where(nodeId => !persistedCanonicalIds.Contains(nodeId))
                .ToArray();

            if (changedCanonicalIds.Length > 0)
            {
                if (removedCanonicalIds.Length > 0)
                {
                    await context.DependencyEdges
                        .Where(edge =>
                            changedCanonicalIds.Contains(edge.CallerId) ||
                            removedCanonicalIds.Contains(edge.CalleeId))
                        .ExecuteDeleteAsync(ct);
                }
                else
                {
                    await context.DependencyEdges
                        .Where(edge => changedCanonicalIds.Contains(edge.CallerId))
                        .ExecuteDeleteAsync(ct);
                }
            }

            foreach (var changedFilePath in changedFilePaths)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM CodeNodeSearch WHERE RelativeFilePath = {changedFilePath}",
                    ct);
            }

            if (changedFilePaths.Length > 0)
            {
                await context.CodeNodes
                    .Where(codeNode => changedFilePaths.Contains(codeNode.RelativeFilePath))
                    .ExecuteDeleteAsync(ct);
            }

            context.ChangeTracker.Clear();

            if (codeNodes.Length > 0)
            {
                await context.CodeNodes.AddRangeAsync(codeNodes, ct);
            }

            if (aggregatedEdges.Count > 0)
            {
                await context.DependencyEdges.AddRangeAsync(aggregatedEdges, ct);
            }

            await context.SaveChangesAsync(ct);

            foreach (var codeNode in codeNodes)
            {
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     INSERT OR REPLACE INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, Summary, RelativeFilePath)
                     VALUES ({codeNode.Id}, {codeNode.CanonicalId}, {codeNode.DisplayName}, {codeNode.FullyQualifiedName}, {codeNode.Summary}, {codeNode.RelativeFilePath})
                     """,
                    ct);
            }

            await transaction.CommitAsync(ct);

            persistActivity.AddTag("index.code_node.count", codeNodes.Length);
            persistActivity.AddTag("index.dependency.count", aggregatedEdges.Count);
            persistActivity.AddTag("index.diagnostic.count", aggregatedDiagnostics.Count);
            trace.AddTag("index.code_node.count", codeNodes.Length);
            trace.AddTag("index.dependency.count", aggregatedEdges.Count);
            trace.AddTag("index.diagnostic.count", aggregatedDiagnostics.Count);

        }
        catch (Exception e)
        {
            trace.RecordExceptionAndErrorStatus(e);
            throw;
        }
    }

    private async Task PopulateEmbeddings(
        IReadOnlyList<CodeNode> codeNodes,
        IProgress<EmbeddingGenerationProgress>? progress,
        CancellationToken ct)
    {
        using var trace = SharpSenseTraceSpan.Start("index.embeddings");
        trace.AddTag("index.embedding.count", codeNodes.Count);

        try
        {
            var embeddingSources = codeNodes
                .Select(
                    static codeNode => string.IsNullOrWhiteSpace(codeNode.Summary)
                        ? codeNode.FullyQualifiedName
                        : $"{codeNode.FullyQualifiedName}\n{codeNode.Summary}\n{codeNode.RelativeFilePath}")
                .ToArray();
            var embeddings = await embeddingsService.GenerateBatch(embeddingSources, progress, ct);

            if (embeddings.Count != codeNodes.Count)
            {
                throw new InvalidOperationException("The embeddings service returned an unexpected number of vectors.");
            }

            for (var index = 0; index < codeNodes.Count; index++)
            {
                codeNodes[index].VectorEmbedding = embeddings[index].Vector;
            }
        }
        catch (Exception e)
        {
            trace.RecordExceptionAndErrorStatus(e);
            throw;
        }
    }

    private string GetRequiredTargetPath()
    {
        if (string.IsNullOrWhiteSpace(_cliOptions.TargetPath))
        {
            throw new InvalidOperationException("A target path must be configured before indexing can start.");
        }

        if (Path.IsPathRooted(_cliOptions.TargetPath))
        {
            return Path.GetFullPath(_cliOptions.TargetPath);
        }

        if (string.IsNullOrWhiteSpace(_cliOptions.RepositoryRoot))
        {
            throw new InvalidOperationException("A repository root must be configured to resolve a relative target path.");
        }

        return Path.GetFullPath(Path.Combine(_cliOptions.RepositoryRoot, _cliOptions.TargetPath));
    }

    private void NormalizePersistedPaths(
        IEnumerable<ProjectNode> projectNodes,
        IEnumerable<CodeNode> codeNodes)
    {
        foreach (var projectNode in projectNodes)
        {
            projectNode.RelativeFilePath = workspace.ToRepositoryRelativePath(projectNode.RelativeFilePath);
        }

        foreach (var codeNode in codeNodes)
        {
            codeNode.RelativeFilePath = workspace.ToRepositoryRelativePath(codeNode.RelativeFilePath);
        }
    }

    private string[] GetAffectedRelativePaths(IReadOnlyList<WorkspaceFileChange> changedFiles)
    {
        var pathComparer = GetPathComparer();
        var relativePaths = new HashSet<string>(pathComparer);

        foreach (var changedFile in changedFiles)
        {
            foreach (var affectedPath in changedFile.GetAffectedPaths())
            {
                if (!workspace.TryToRepositoryRelativePath(affectedPath, out var relativePath) ||
                    !IsIncrementalTargetPath(relativePath))
                {
                    continue;
                }

                relativePaths.Add(relativePath);
            }
        }

        return [.. relativePaths.OrderBy(static path => path, pathComparer)];
    }

    private async Task AssignPersistedCodeNodeIds(
        IReadOnlyCollection<CodeNode> codeNodes,
        CancellationToken ct)
    {
        var persistedIdsByCanonicalId = await context.CodeNodes
            .AsNoTracking()
            .Select(static codeNode => new PersistedCodeNodeIdentity(codeNode.Id, codeNode.CanonicalId))
            .ToDictionaryAsync(
                static codeNode => codeNode.CanonicalId,
                static codeNode => codeNode.Id,
                StringComparer.Ordinal,
                ct);

        AssignPersistedCodeNodeIds(codeNodes, persistedIdsByCanonicalId);
    }

    private static void AssignPersistedCodeNodeIds(
        IEnumerable<CodeNode> codeNodes,
        IEnumerable<PersistedCodeNodeIdentity> persistedCodeNodes)
    {
        var persistedIdsByCanonicalId = persistedCodeNodes
            .ToDictionary(
                static codeNode => codeNode.CanonicalId,
                static codeNode => codeNode.Id,
                StringComparer.Ordinal);

        AssignPersistedCodeNodeIds(codeNodes, persistedIdsByCanonicalId);
    }

    private static void AssignPersistedCodeNodeIds(
        IEnumerable<CodeNode> codeNodes,
        IReadOnlyDictionary<string, int> persistedIdsByCanonicalId)
    {
        foreach (var codeNode in codeNodes)
        {
            if (persistedIdsByCanonicalId.TryGetValue(codeNode.CanonicalId, out var persistedId))
            {
                codeNode.Id = persistedId;
            }
        }
    }

    private static bool IsIncrementalTargetPath(string path)
    {
        return string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase) ||
               MarkdownIndexer.IsMarkdownDocumentPath(path);
    }

    private static StringComparer GetPathComparer()
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record PersistedCodeNodeIdentity(int Id, string CanonicalId);
}
