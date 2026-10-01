using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.GraphStats.Abstractions;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;

namespace SharpSense.Application.Tests.Indexing.Support;

internal sealed class IndexingHandlers : IDisposable
{
    private readonly List<WorkspaceExtractionCoordinator> _coordinators = [];

    public IndexWorkspaceCommandHandler Create(
        IEnumerable<ILanguageExtractor>? extractors = null,
        IEmbeddingGenerator? embeddingGenerator = null,
        IKnowledgeGraphRepository? repository = null,
        IIndexingWorkspacePaths? workspacePaths = null,
        WorkspaceExecutionOptions? options = null,
        IIndexRunStore? runStore = null)
    {
        workspacePaths ??= new Mock<IIndexingWorkspacePaths>(MockBehavior.Strict).Object;
        var coordinator = new WorkspaceExtractionCoordinator(
            extractors ?? [],
            workspacePaths,
            Mock.Of<IWorkspaceChangeFilter>(),
            NullLogger<WorkspaceExtractionCoordinator>.Instance);
        _coordinators.Add(coordinator);

        return new IndexWorkspaceCommandHandler(
            embeddingGenerator ?? new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object,
            repository ?? new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict).Object,
            workspacePaths,
            Options.Create(options ?? new WorkspaceExecutionOptions
            {
                WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "SharpSense.sln")],
                RepositoryRoot = "/repo"
            }),
            coordinator,
            runStore ?? Mock.Of<IIndexRunStore>(),
            NullLogger<IndexWorkspaceCommandHandler>.Instance);
    }

    public UpdateWorkspaceFilesCommandHandler CreateIncremental(
        IEnumerable<ILanguageExtractor>? extractors = null,
        IEmbeddingGenerator? embeddingGenerator = null,
        IKnowledgeGraphRepository? repository = null,
        IIndexingWorkspacePaths? workspacePaths = null,
        WorkspaceExecutionOptions? options = null,
        IIndexRunStore? runStore = null)
        => new(
            Create(extractors, embeddingGenerator, repository, workspacePaths, options, runStore),
            Mock.Of<IWorkspaceChangeFilter>(filter => filter.IsRelevant(It.IsAny<IReadOnlyList<WorkspaceFileChange>>()) == true));

    public void Dispose()
    {
        foreach (var coordinator in _coordinators)
        {
            coordinator.Dispose();
        }
    }
}
