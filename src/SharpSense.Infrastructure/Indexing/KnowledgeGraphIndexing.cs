using Microsoft.EntityFrameworkCore;
using SharpSense.Application.Features.Indexing.Contracts;
using SharpSense.Application.Features.Indexing.IndexSolution;
using SharpSense.Application.Features.Indexing.Infrastructure;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Embeddings;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Indexing;

public sealed class KnowledgeGraphIndexing(
    IRoslynSolutionAnalysisEngine analysisEngine,
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
            var extractionPayload = await analysisEngine
                .ExtractAsync(
                    absoluteSolutionPath,
                    workspace,
                    new RoslynWorkspaceOptions(),
                    command.Progress,
                    ct);

            extractActivity.AddTag("index.project.count", extractionPayload.Projects.Count);
            extractActivity.AddTag("index.code_node.count", extractionPayload.CodeNodes.Count);
            extractActivity.AddTag("index.dependency.count", extractionPayload.Edges.Count);
            var codeNodes = extractionPayload.CodeNodes.ToArray();
            var projectCount = extractionPayload.Projects.Count;

            if (command.IncludeEmbeddings && codeNodes.Length > 0)
            {
                command.Progress?.Report(new IndexingProgress("Embedding phase...", projectCount, projectCount));
                await PopulateEmbeddingsAsync(codeNodes, command.EmbeddingProgress, ct);
            }

            NormalizePersistedPaths(extractionPayload.Projects, codeNodes);
            command.Progress?.Report(new IndexingProgress("Persisting index...", projectCount, projectCount));

            using (var persistActivity = SharpSenseTraceSpan.Start("index.persist"))
            {
                await using var transaction = await context.Database.BeginTransactionAsync(ct);

                await context.DependencyEdges.ExecuteDeleteAsync(ct);
                await context.CodeNodes.ExecuteDeleteAsync(ct);
                await context.ProjectNodes.ExecuteDeleteAsync(ct);

                await context.ProjectNodes.AddRangeAsync(extractionPayload.Projects, ct);
                await context.CodeNodes.AddRangeAsync(codeNodes, ct);
                await context.DependencyEdges.AddRangeAsync(extractionPayload.Edges, ct);
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

                persistActivity.AddTag("index.project.count", extractionPayload.Projects.Count);
                persistActivity.AddTag("index.code_node.count", codeNodes.Length);
                persistActivity.AddTag("index.dependency.count", extractionPayload.Edges.Count);
            }

            trace.AddTag("index.project.count", extractionPayload.Projects.Count);
            trace.AddTag("index.code_node.count", codeNodes.Length);
            trace.AddTag("index.dependency.count", extractionPayload.Edges.Count);

            return new IndexingSummary(
                absoluteSolutionPath,
                extractionPayload.Projects.Count,
                codeNodes.Length,
                extractionPayload.Edges.Count);
        }
        catch (Exception e)
        {
            trace.RecordExceptionAndErrorStatus(e);
            throw;
        }
    }

    private async Task PopulateEmbeddingsAsync(
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
