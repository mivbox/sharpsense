using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexWorkspace;
using SharpSense.Application.Indexing.IndexWorkspace.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles;
using SharpSense.Application.Indexing.UpdateWorkspaceFiles.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Storage;
using SharpSense.Testkit;
using System.IO.Abstractions;
using System.Text.Json;

namespace SharpSense.Infrastructure.Tests.Indexing.CSharp;

public sealed class CSharpWorkspaceRefreshTests
{
    [Fact]
    public async Task WhenSignaturesMembershipAndProjectsChange_ThenWatchMatchesCleanFullIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(
            Path.GetTempPath(),
            "sharpsense-csharp-refresh-" + Guid.NewGuid()
                .ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Write("App/App.csproj", Project());
            await Write("Other/Other.csproj", Project());
            await Write("Workspace.slnx", Solution(includeOther: true));
            await Write("App/Api.cs", "namespace Fixture; public static class Api { public static int Read(int value) => 1; }");
            await Write(
                "App/Consumer.cs",
                "namespace Fixture; public static class Consumer { public static int Call() => Api.Read(1); }");
            await Write("App/Excluded.cs", "namespace Fixture; public class Excluded {}");
            await Write("App/Deleted.cs", "namespace Fixture; public class Deleted {}");
            await Write("App/PartA.cs", "namespace Fixture; public partial class Shared { public void A() {} }");
            await Write("App/PartB.cs", "namespace Fixture; public partial class Shared { public void B() {} }");
            await Write("Other/Other.cs", "namespace Other; public class RemovedProjectType {}");
            var fileSystem = new FileSystem();
            var workspace = new RepositoryWorkspace(root, Path.Combine(root, "unused.db"), fileSystem);
            var target = Path.Combine(root, "Workspace.slnx");
            var paths = new IndexingWorkspacePaths(workspace);
            var options = Options.Create(new WorkspaceExecutionOptions
            {
                WorkspaceSources = [new(WorkspaceSourceKind.CSharp, target)],
                RepositoryRoot = root,
                SkipEmbeddings = true
            });
            var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object;
            using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), fileSystem);
            var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);
            var resolver = new CSharpWorkspaceTargetResolver(workspace, fileSystem);
            var extractor = new CSharpLanguageExtractor(loader, engine, workspace, resolver);
            await using var database = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
            var repository = new KnowledgeGraphRepository(database.CreateDbContextFactory());
            var index = new IndexWorkspaceCommandHandler(
                embeddings,
                repository,
                paths,
                options,
                new WorkspaceExtractionCoordinator(
                    [extractor],
                    paths,
                    Moq.Mock.Of<IWorkspaceChangeFilter>(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance),
                Mock.Of<SharpSense.Application.GraphStats.Abstractions.IIndexRunStore>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance);
            var update = new UpdateWorkspaceFilesCommandHandler(
                new IndexWorkspaceCommandHandler(
                    embeddings,
                    repository,
                    paths,
                    options,
                    new WorkspaceExtractionCoordinator(
                        [extractor],
                        paths,
                        Moq.Mock.Of<IWorkspaceChangeFilter>(),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance),
                    Mock.Of<SharpSense.Application.GraphStats.Abstractions.IIndexRunStore>(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance),
                Mock.Of<IWorkspaceChangeFilter>(filter => filter.IsRelevant(It.IsAny<IReadOnlyList<WorkspaceFileChange>>()) == true));

            var initial = await index.Handle(new IndexWorkspaceCommand(), ct);
            initial.IsSuccess.Should().BeTrue(Errors(initial));
            var callerId = await ConsumerId();

            await Write("App/Api.cs", "namespace Fixture; public static class Api { public static int Read(long value) => 2; }");
            var signatureUpdate = await update.Handle(
                new UpdateWorkspaceFilesCommand(
                [new(WorkspaceFileChangeAction.Modified, NewPath: Path.Combine(root, "App/Api.cs"))]),
                ct);
            signatureUpdate.IsSuccess.Should().BeTrue(Errors(signatureUpdate));
            (await ConsumerId()).Should().Be(callerId);
            var afterSignature = await repository.GetPersistedCodeNodes(ct);
            afterSignature.Should().Contain(node => node.FullyQualifiedName.Contains("Read(long)", StringComparison.Ordinal));
            afterSignature.Should().NotContain(node => node.FullyQualifiedName.Contains("Read(int)", StringComparison.Ordinal));
            await AssertMatchesFreshIndex();
            await using (var context = await database.GetContext(ct))
            {
                var targets = await (from edge in context.DependencyEdges
                                     join node in context.CodeNodes on edge.CalleeNodeId equals node.Id
                                     where edge.CallerNodeId == callerId && edge.EdgeType == EdgeType.MethodCall
                                     select node.FullyQualifiedName).ToArrayAsync(ct);
                targets.Should().Contain(name => name.Contains("Read(long)", StringComparison.Ordinal));
            }

            await Write("App/App.csproj", Project("<Compile Remove=\"Excluded.cs\" />"));
            File.Delete(Path.Combine(root, "App/Deleted.cs"));
            File.Delete(Path.Combine(root, "App/PartA.cs"));
            var membershipUpdate = await update.Handle(
                new UpdateWorkspaceFilesCommand(
            [
                new(WorkspaceFileChangeAction.Modified, NewPath: Path.Combine(root, "App/App.csproj")),
                new(WorkspaceFileChangeAction.Deleted, OldPath: Path.Combine(root, "App/Deleted.cs")),
                new(WorkspaceFileChangeAction.Deleted, OldPath: Path.Combine(root, "App/PartA.cs"))
            ]),
                ct);
            membershipUpdate.IsSuccess.Should().BeTrue(Errors(membershipUpdate));
            var afterMembership = await repository.GetPersistedCodeNodes(ct);
            afterMembership.Where(node => node.RelativeFilePath is "App/Excluded.cs" or "App/Deleted.cs" or "App/PartA.cs").Should().BeEmpty();
            afterMembership.Should().Contain(node => node.FullyQualifiedName == "Fixture.Shared" && node.RelativeFilePath == "App/PartB.cs");
            await AssertMatchesFreshIndex();

            await Write("Workspace.slnx", Solution(includeOther: false));
            var projectUpdate = await update.Handle(
                new UpdateWorkspaceFilesCommand(
                [new(WorkspaceFileChangeAction.Modified, NewPath: target)]),
                ct);
            projectUpdate.IsSuccess.Should().BeTrue(Errors(projectUpdate));
            (await repository.GetPersistedCodeNodes(ct)).Should().NotContain(node => node.FullyQualifiedName.Contains("RemovedProjectType", StringComparison.Ordinal));
            await using (var context = await database.GetContext(ct))
            {
                (await context.ProjectNodes.CountAsync(ct)).Should().Be(1);
            }

            await AssertMatchesFreshIndex();

            var savedSnapshot = await Snapshot(database, repository);
            await Write("Workspace.slnx", "<not-valid-xml");
            var failedUpdate = await update.Handle(
                new UpdateWorkspaceFilesCommand(
                [new(WorkspaceFileChangeAction.Modified, NewPath: target)]),
                ct);
            failedUpdate.IsFailed.Should().BeTrue();
            (await Snapshot(database, repository)).Should().Be(savedSnapshot);

            async Task<int> ConsumerId()
            {
                await using var context = await database.GetContext(ct);

                return await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Consumer.Call()")
                    .Select(node => node.Id)
                    .SingleAsync(ct);
            }

            async Task AssertMatchesFreshIndex()
            {
                using var freshLoader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), fileSystem);
                var freshExtractor = new CSharpLanguageExtractor(freshLoader, engine, workspace, resolver);
                await using var freshDatabase = new InMemoryContextFactory<SharpSenseDbContext>(options => new SharpSenseDbContext(options), new(UseMigrations: true, LoadVectorExtension: true));
                var freshRepository = new KnowledgeGraphRepository(freshDatabase.CreateDbContextFactory());
                var freshIndex = new IndexWorkspaceCommandHandler(
                    embeddings,
                    freshRepository,
                    paths,
                    options,
                    new WorkspaceExtractionCoordinator(
                        [freshExtractor],
                        paths,
                        Moq.Mock.Of<IWorkspaceChangeFilter>(),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<WorkspaceExtractionCoordinator>.Instance),
                    Mock.Of<SharpSense.Application.GraphStats.Abstractions.IIndexRunStore>(),
                    Microsoft.Extensions.Logging.Abstractions.NullLogger<IndexWorkspaceCommandHandler>.Instance);
                var result = await freshIndex.Handle(new IndexWorkspaceCommand(), ct);
                result.IsSuccess.Should().BeTrue(Errors(result));
                (await Snapshot(database, repository)).Should().Be(await Snapshot(freshDatabase, freshRepository));
            }

            async Task<string> Snapshot(InMemoryContextFactory<SharpSenseDbContext> factory, KnowledgeGraphRepository graphRepository)
            {
                var nodes = await graphRepository.GetPersistedCodeNodes(ct);
                await using var context = await factory.GetContext(ct);
                var edges = await (from edge in context.DependencyEdges
                                   join caller in context.GraphNodes on edge.CallerNodeId equals caller.Id
                                   join callee in context.GraphNodes on edge.CalleeNodeId equals callee.Id
                                   select new
                                   {
                                       Caller = caller.CanonicalId,
                                       Callee = callee.CanonicalId,
                                       edge.EdgeType,
                                       edge.Metadata
                                   })
                    .ToArrayAsync(ct);
                var projects = await (from project in context.ProjectNodes
                                      join graph in context.GraphNodes on project.Id equals graph.Id
                                      join document in context.Documents on project.ProjectDocumentId equals document.Id
                                      select new
                                      {
                                          graph.CanonicalId,
                                          project.Name,
                                          document.RelativePath,
                                          project.ContentHash
                                      })
                    .ToArrayAsync(ct);

                return JsonSerializer.Serialize(new
                {
                    Nodes = nodes.OrderBy(node => node.CanonicalId, StringComparer.Ordinal),
                    Edges = edges.OrderBy(edge => edge.Caller, StringComparer.Ordinal)
                        .ThenBy(edge => edge.Callee, StringComparer.Ordinal)
                        .ThenBy(edge => edge.EdgeType),
                    Projects = projects.OrderBy(project => project.CanonicalId, StringComparer.Ordinal)
                });
            }

            async Task Write(string relativePath, string content)
            {
                var path = Path.Combine(root, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, content, ct);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Project(string items = "")
        => $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{items}</ItemGroup></Project>""";

    private static string Solution(bool includeOther)
        => "<Solution><Project Path=\"App/App.csproj\" />" +
           (includeOther ? "<Project Path=\"Other/Other.csproj\" />" : "") + "</Solution>";

    private static string Errors(FluentResults.ResultBase result)
        => string.Join("; ", result.Errors.Select(error => error.Message));
}
