using FluentResults;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.Notifications;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Errors;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;

namespace SharpSense.Application.Indexing.IndexTarget;

public sealed class IndexTargetCommandHandler(
    IEnumerable<ILanguageExtractor> extractors,
    IEmbeddingGenerator embeddingGenerator,
    IKnowledgeGraphRepository knowledgeGraphRepository,
    IIndexingWorkspacePaths workspacePaths,
    IOptions<SharpSenseCliOptions> cliOptions,
    IIndexRunStore? indexRunStore = null,
    ILogger<IndexTargetCommandHandler>? logger = null,
    IWorkspaceChangeFilter? workspaceChangeFilter = null,
    WorkspaceExtractionCoordinator? workspaceExtraction = null)
    : ICommandHandler<IndexTargetCommand, Result<IndexTargetOutcome>>
{
    private readonly SharpSenseCliOptions _cliOptions = cliOptions?.Value ?? throw new ArgumentNullException(nameof(cliOptions));

    private readonly WorkspaceExtractionCoordinator _workspaceExtraction = workspaceExtraction ?? new(extractors, workspacePaths, workspaceChangeFilter);

    public async Task<Result<IndexTargetOutcome>> Handle(IndexTargetCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var analysis = command.Notifier is null ? null : new AnalysisOperation(command.Notifier,
            command.ChangedFiles is null ? AnalysisOperationKind.Full : AnalysisOperationKind.Incremental);

        if (!string.IsNullOrWhiteSpace(_cliOptions.WorkspaceId) && _cliOptions.WorkspaceSources.Count == 0)
        {
            analysis?.Failed("Workspace has no selected sources. Add projects, TypeScript sources, or documentation before indexing.");
            return Result.Fail(new ServiceError(
                ServiceErrorCode.FailedPrecondition,
                "Workspace has no selected sources. Add projects, TypeScript sources, or documentation before indexing."));
        }

        string absoluteTargetPath;
        try
        {
            absoluteTargetPath = _cliOptions.WorkspaceSources.Count > 0
                ? workspacePaths.RootPath
                : workspacePaths.GetRequiredTargetPath(GetRequiredTargetPath());
        }
        catch (Exception exception)
        {
            analysis?.Failed(exception.Message);
            throw;
        }

        using var trace = SharpSenseTraceSpan.Start("index.target");
        trace.AddTag("target.path", absoluteTargetPath);
        trace.AddTag("repository.root", workspacePaths.RootPath);
        trace.AddTag("index.include_embeddings", !_cliOptions.SkipEmbeddings);
        await using var run = new IndexingRunDiagnostics(
            indexRunStore,
            command.ChangedFiles is null ? "full" : "incremental",
            absoluteTargetPath,
            logger);

        try
        {
            analysis?.Phase(AnalysisPhase.Discovery, "Preparing workspace sources...");
            using var extractionLease = _cliOptions.WorkspaceSources.Count > 0
                ? await _workspaceExtraction.Acquire(ct)
                : null;
            WorkspaceExtractionBatch? workspaceBatch = null;
            using var extractActivity = SharpSenseTraceSpan.Start("index.extract");
            Result<ExtractedNodes> extractionResult;
            using (run.Measure("extraction"))
            {
                analysis?.Phase(AnalysisPhase.Extraction, "Analyzing workspace sources...");
                var context = new ExtractionContext(absoluteTargetPath, command.Progress, command.ChangedFiles);
                if (_cliOptions.WorkspaceSources.Count > 0)
                {
                    var extraction = await _workspaceExtraction.Extract(_cliOptions.WorkspaceId, _cliOptions.WorkspaceSources, context, ct, analysis);
                    workspaceBatch = extraction.IsSuccess ? extraction.Value : null;
                    extractionResult = extraction.IsSuccess ? Result.Ok(extraction.Value.Graph) : Result.Fail(extraction.Errors);
                }
                else
                {
                    extractionResult = await Extract(context, extractActivity, ct, analysis);
                }
            }

            if (extractionResult.IsFailed)
            {
                ct.ThrowIfCancellationRequested();
                trace.SetError();
                run.Failed(extractionResult.Errors);
                analysis?.Failed(string.Join(Environment.NewLine, extractionResult.Errors.Take(20).Select(error => error.Message)));
                return Result.Fail(extractionResult.Errors);
            }

            var extractedNodes = extractionResult.Value;
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
                analysis?.Phase(AnalysisPhase.Embeddings, _cliOptions.SkipEmbeddings
                    ? "Reusing available embeddings..." : "Preparing embeddings...");
                if (!_cliOptions.SkipEmbeddings)
                {
                    command.Progress?.Report(new IndexingProgress("Embedding phase...", projectCount, projectCount));
                }

                var persistedCodeNodes = _cliOptions.DisableEmbeddingCache
                    ? []
                    : await knowledgeGraphRepository.GetPersistedCodeNodes(ct);
                extractedNodes = extractedNodes with
                {
                    CodeNodes = await CodeNodeEmbeddingCoordinator.Populate(
                        extractedNodes.CodeNodes,
                        persistedCodeNodes,
                        _cliOptions.SkipEmbeddings,
                        _cliOptions.DisableEmbeddingCache,
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

            extractedNodes = NormalizePersistedPaths(extractedNodes);
            analysis?.Phase(AnalysisPhase.Persistence, "Saving graph...");
            command.Progress?.Report(new IndexingProgress("Persisting index...", projectCount, projectCount));

            using (run.Measure("persistence"))
            {
                await knowledgeGraphRepository.ReplaceTarget(extractedNodes, ct);
            }
            if (workspaceBatch is not null)
            {
                _workspaceExtraction.Commit(workspaceBatch);
            }
            run.Succeeded();
            analysis?.Committed(extractedNodes);

            trace.AddTag("index.project.count", extractedNodes.Projects.Count);
            trace.AddTag("index.code_node.count", extractedNodes.CodeNodes.Count);
            extractActivity.AddTag("index.document_node.count", documentNodeCount);
            trace.AddTag("index.dependency.count", extractedNodes.Edges.Count);
            trace.AddTag("index.diagnostic.count", extractedNodes.Diagnostics.Count);

            return Result.Ok(new IndexTargetOutcome(
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
            return Result.Fail(new ServiceError(
                ServiceErrorCode.FailedPrecondition,
                $"Indexing of '{absoluteTargetPath}' was cancelled."));
        }
        catch (Exception exception)
        {
            trace.RecordExceptionAndErrorStatus(exception);
            run.Failed(exception);
            analysis?.Failed(exception.Message);
            return Result.Fail(new ServiceError(
                ServiceErrorCode.InternalError,
                $"Indexing of '{absoluteTargetPath}' failed: {exception.Message}"));
        }
    }

    private async Task<Result<ExtractedNodes>> Extract(
        ExtractionContext context,
        SharpSenseTraceSpan extractActivity,
        CancellationToken ct,
        AnalysisOperation? analysis)
    {
        var aggregatedProjects = new List<IndexedProject>();
        var aggregatedCodeNodes = new List<IndexedCodeNode>();
        var aggregatedEdges = new List<IndexedDependency>();
        var aggregatedDiagnostics = new List<string>();

        foreach (var extractor in extractors)
        {
            var source = analysis is not null && extractor.SourceKind is { } kind
                ? new AnalysisSource(kind, context.TargetPath) : null;
            if (source is not null)
            {
                analysis?.SourceStarted(source);
            }

            var extractionResult = await extractor.Extract(context with
            {
                Progress = analysis?.SourceProgress(source, context.Progress) ?? context.Progress
            }, ct);
            if (extractionResult.IsFailed)
            {
                return extractionResult;
            }

            var extractedNodes = extractionResult.Value;
            analysis?.SourceCompleted(source);

            aggregatedProjects.AddRange(extractedNodes.Projects);
            aggregatedCodeNodes.AddRange(extractedNodes.CodeNodes);
            aggregatedEdges.AddRange(extractedNodes.Edges);
            aggregatedDiagnostics.AddRange(extractedNodes.Diagnostics);

            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.project.count", extractedNodes.Projects.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.code_node.count", extractedNodes.CodeNodes.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.dependency.count", extractedNodes.Edges.Count);
            extractActivity.AddTag($"index.extractor.{extractor.ExtractorName}.diagnostic.count", extractedNodes.Diagnostics.Count);
        }

        return Result.Ok(new ExtractedNodes(
            [
                .. aggregatedProjects
                    .OrderBy(static project => project.Name, StringComparer.Ordinal)
                    .ThenBy(static project => project.Id, StringComparer.Ordinal)
            ],
            [
                .. aggregatedCodeNodes
                    .OrderBy(static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal)
                    .ThenBy(static codeNode => codeNode.CanonicalId, StringComparer.Ordinal)
            ],
            [
                .. aggregatedEdges
                    .OrderBy(static edge => edge.CallerId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.CalleeId, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.EdgeType)
            ],
            aggregatedDiagnostics));
    }

    private ExtractedNodes NormalizePersistedPaths(ExtractedNodes extractedNodes)
    {
        return extractedNodes with
        {
            Projects =
            [
                .. extractedNodes.Projects.Select(
                    project => project with
                    {
                        RelativeFilePath = workspacePaths.ToRepositoryRelativePath(project.RelativeFilePath)
                    })
            ],
            CodeNodes =
            [
                .. extractedNodes.CodeNodes.Select(
                    codeNode => codeNode with
                    {
                        RelativeFilePath = workspacePaths.ToRepositoryRelativePath(codeNode.RelativeFilePath)
                    })
            ]
        };
    }

    private string GetRequiredTargetPath()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(_cliOptions.TargetPath);
        return _cliOptions.TargetPath;
    }
}
