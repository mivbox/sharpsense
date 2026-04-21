using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Features.Indexing.IndexSolution;
using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class KnowledgeGraphIndexing(
    IEnumerable<ILanguageExtractor> extractors,
    IEmbeddingGenerator embeddingsService,
    SharpSenseDbContext context,
    IRepositoryWorkspace workspace)
    : IKnowledgeGraphIndexing
{
    public async Task<IndexingSummary> Index(IndexSolutionCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SolutionPath);

        var absoluteSolutionPath = Path.GetFullPath(command.SolutionPath);

        using var trace = SharpSenseTraceSpan.Start("index.solution");
        trace.AddTag("solution.path", absoluteSolutionPath);
        trace.AddTag("repository.root", workspace.RootPath);
        trace.AddTag("index.include_embeddings", command.IncludeEmbeddings);

        try
        {
            using var extractActivity = SharpSenseTraceSpan.Start("index.extract");
            var extractionContext = new ExtractionContext(absoluteSolutionPath, command.Progress);
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
                .ThenBy(static codeNode => codeNode.Id, StringComparer.Ordinal)
                .ToArray();
            var projectCount = aggregatedProjects.Count;
            var documentNodeCount = codeNodes.Count(static codeNode => codeNode.NodeType == NodeType.Document);

            extractActivity.AddTag("index.project.count", aggregatedProjects.Count);
            extractActivity.AddTag("index.code_node.count", codeNodes.Length);
            extractActivity.AddTag("index.document_node.count", documentNodeCount);
            extractActivity.AddTag("index.dependency.count", aggregatedEdges.Count);
            extractActivity.AddTag("index.diagnostic.count", aggregatedDiagnostics.Count);

            if (command.IncludeEmbeddings && codeNodes.Length > 0)
            {
                command.Progress?.Report(new IndexingProgress("Embedding phase...", projectCount, projectCount));
                await PopulateEmbeddings(codeNodes, command.EmbeddingProgress, ct);
            }

            NormalizePersistedPaths(aggregatedProjects, codeNodes);
            command.Progress?.Report(new IndexingProgress("Persisting index...", projectCount, projectCount));

            using (var persistActivity = SharpSenseTraceSpan.Start("index.persist"))
            {
                await using var transaction = await context.Database.BeginTransactionAsync(ct);

                await context.DependencyEdges.ExecuteDeleteAsync(ct);
                await context.CodeNodes.ExecuteDeleteAsync(ct);
                await context.ProjectNodes.ExecuteDeleteAsync(ct);

                await context.ProjectNodes.AddRangeAsync(aggregatedProjects, ct);
                await context.CodeNodes.AddRangeAsync(codeNodes, ct);
                await context.DependencyEdges.AddRangeAsync(aggregatedEdges, ct);
                await context.SaveChangesAsync(ct);

                await context.Database.ExecuteSqlRawAsync(
                    """
                    DELETE FROM CodeNodeSearch;
                    INSERT INTO CodeNodeSearch (Id, FullyQualifiedName, Summary, RelativeFilePath)
                    SELECT Id, FullyQualifiedName, Summary, RelativeFilePath
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

            return new IndexingSummary(
                absoluteSolutionPath,
                aggregatedProjects.Count,
                codeNodes.Length,
                aggregatedEdges.Count);
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
}
