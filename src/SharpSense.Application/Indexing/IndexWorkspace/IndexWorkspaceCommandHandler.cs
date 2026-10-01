using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Indexing.IndexWorkspace;

internal sealed class IndexWorkspaceCommandHandler(
    IEmbeddingGenerator embeddingGenerator,
    IKnowledgeGraphRepository knowledgeGraphRepository,
    IIndexingWorkspacePaths workspacePaths,
    IOptions<WorkspaceExecutionOptions> executionOptions,
    WorkspaceExtractionCoordinator workspaceExtraction,
    IIndexRunStore indexRunStore,
    ILogger<IndexWorkspaceCommandHandler> logger)
    : ICommandHandler<IndexWorkspaceCommand, Result<IndexWorkspaceOutcome>>
{
    private readonly WorkspaceExecutionOptions _options = executionOptions.Value;

    public async Task<Result<IndexWorkspaceOutcome>> Handle(IndexWorkspaceCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var analysis = command.Notifier is null
            ? null
            : new AnalysisOperation(
                command.Notifier,
                command.ChangedFiles is null ? AnalysisOperationKind.Full : AnalysisOperationKind.Incremental);

        if (_options.WorkspaceSources.Count == 0)
        {
            analysis?.Failed("Workspace has no selected sources. Add projects, TypeScript sources, or documentation before indexing.");

            return Result.Fail(
                new ServiceError(
                    ServiceErrorCode.FailedPrecondition,
                    "Workspace has no selected sources. Add projects, TypeScript sources, or documentation before indexing."));
        }

        var absoluteTargetPath = workspacePaths.RootPath;

        using var trace = SharpSenseTraceSpan.Start("index.workspace");
        trace.AddTag("target.path", absoluteTargetPath);
        trace.AddTag("repository.root", workspacePaths.RootPath);
        trace.AddTag("index.include_embeddings", !_options.SkipEmbeddings);
        await using var run = new IndexingRunDiagnostics(
            indexRunStore,
            command.ChangedFiles is null ? "full" : "incremental",
            absoluteTargetPath,
            logger);

        try
        {
            analysis?.Phase(AnalysisPhase.Discovery, "Preparing workspace sources...");
            using var extractionLease = await workspaceExtraction.Acquire(ct);
            using var extractActivity = SharpSenseTraceSpan.Start("index.extract");
            Result<WorkspaceExtractionBatch> extractionResult;
            using (run.Measure("extraction"))
            {
                analysis?.Phase(AnalysisPhase.Extraction, "Analyzing workspace sources...");
                var context = new ExtractionContext(absoluteTargetPath, command.Progress, command.ChangedFiles);
                extractionResult = await workspaceExtraction.Extract(
                    _options.WorkspaceId,
                    _options.WorkspaceSources,
                    context,
                    ct,
                    analysis);
            }

            if (extractionResult.IsFailed)
            {
                ct.ThrowIfCancellationRequested();
                trace.SetError();
                run.Failed(extractionResult.Errors);
                analysis?.Failed(string.Join(
                    Environment.NewLine,
                    extractionResult.Errors.Take(20)
                        .Select(error => error.Message)));

                return Result.Fail(extractionResult.Errors);
            }

            var workspaceBatch = extractionResult.Value;
            var extractedNodes = workspaceBatch.Graph;
            run.Extracted(extractedNodes);
            analysis?.Diagnostics(extractedNodes.Diagnostics);
            var projectCount = extractedNodes.Projects.Count;
            var documentNodeCount = extractedNodes.CodeNodes.Count(static codeNode => codeNode.NodeType == NodeType.Document);

            extractActivity.AddTag("index.project.count", extractedNodes.Projects.Count);
            extractActivity.AddTag("index.code_node.count", extractedNodes.CodeNodes.Count);
            extractActivity.AddTag("index.document_node.count", documentNodeCount);
            extractActivity.AddTag("index.dependency.count", extractedNodes.Edges.Count);
            extractActivity.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);

            if (extractedNodes.CodeNodes.Count > 0)
            {
                using var embeddingTiming = run.Measure("embeddings");
                analysis?.Phase(
                    AnalysisPhase.Embeddings,
                    _options.SkipEmbeddings
                        ? "Reusing available embeddings..."
                        : "Preparing embeddings...");
                if (!_options.SkipEmbeddings)
                {
                    command.Progress?.Report(new IndexingProgress("Embedding phase...", projectCount, projectCount));
                }

                var persistedCodeNodes = _options.DisableEmbeddingCache
                    ? []
                    : await knowledgeGraphRepository.GetPersistedCodeNodes(ct);
                extractedNodes = extractedNodes with
                {
                    CodeNodes = await CodeNodeEmbeddingCoordinator.Populate(
                        extractedNodes.CodeNodes,
                        persistedCodeNodes,
                        _options.SkipEmbeddings,
                        _options.DisableEmbeddingCache,
                        embeddingGenerator,
                        analysis?.EmbeddingProgress(command.EmbeddingProgress) ?? command.EmbeddingProgress,
                        ct,
                        (reused, generated) =>
                        {
                            run.Embeddings(reused, generated);
                            analysis?.Embeddings(reused, generated);
                        })
                };
            }

            analysis?.Phase(AnalysisPhase.Persistence, "Saving graph...");
            command.Progress?.Report(new IndexingProgress("Persisting index...", projectCount, projectCount));

            using (run.Measure("persistence"))
            {
                await knowledgeGraphRepository.ReplaceWorkspace(extractedNodes, ct);
            }

            workspaceExtraction.Commit(workspaceBatch);

            run.Succeeded();
            analysis?.Committed(extractedNodes);

            trace.AddTag("index.project.count", extractedNodes.Projects.Count);
            trace.AddTag("index.code_node.count", extractedNodes.CodeNodes.Count);
            extractActivity.AddTag("index.document_node.count", documentNodeCount);
            trace.AddTag("index.dependency.count", extractedNodes.Edges.Count);
            trace.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);

            return Result.Ok(
                new IndexWorkspaceOutcome(
                    ProjectsIndexed: extractedNodes.Projects.Count,
                    CodeNodesPersisted: extractedNodes.CodeNodes.Count,
                    DependencyEdgesPersisted: extractedNodes.Edges.Count,
                    DocumentNodesPersisted: documentNodeCount));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            trace.SetError();
            run.Cancelled();
            analysis?.Cancelled();

            return Result.Fail(
                new ServiceError(
                    ServiceErrorCode.FailedPrecondition,
                    $"Indexing of '{absoluteTargetPath}' was cancelled."));
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            run.Failed(exception);
            analysis?.Failed(exception.Message);

            return Result.Fail(
                new ServiceError(
                    ServiceErrorCode.InternalError,
                    $"Indexing of '{absoluteTargetPath}' failed: {exception.Message}"));
        }
    }
}
