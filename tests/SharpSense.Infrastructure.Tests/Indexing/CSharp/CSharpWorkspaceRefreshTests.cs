using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
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

namespace SharpSense.Infrastructure.Tests.Indexing.CSharp;

public sealed class CSharpWorkspaceRefreshTests
{
    [Fact]
    public async Task WhenSignaturesMembershipAndProjectsChange_ThenWatchMatchesCleanFullIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "sharpsense-csharp-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await Write("App/App.csproj", Project());
            await Write("Other/Other.csproj", Project());
            await Write("Workspace.slnx", Solution(includeOther: true));
            await Write("App/Api.cs", "namespace Fixture; public static class Api { public static int Read(int value) => 1; }");
            await Write("App/Consumer.cs", "namespace Fixture; public static class Consumer { public static int Call() => Api.Read(1); }");
            await Write("App/Excluded.cs", "namespace Fixture; public class Excluded {}");
            await Write("App/Deleted.cs", "namespace Fixture; public class Deleted {}");
            await Write("App/PartA.cs", "namespace Fixture; public partial class Shared { public void A() {} }");
            await Write("App/PartB.cs", "namespace Fixture; public partial class Shared { public void B() {} }");
            await Write("Other/Other.cs", "namespace Other; public class RemovedProjectType {}");
            var fileSystem = new FileSystem();
            var workspace = new RepositoryWorkspace(root, Path.Combine(root, "unused.db"), fileSystem);
            var target = Path.Combine(root, "Workspace.slnx");
            var paths = new IndexingWorkspacePaths(workspace);
            var options = Options.Create(new SharpSenseCliOptions
            {
                TargetPath = target, RepositoryRoot = root, SkipEmbeddings = true
            });
            var embeddings = new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object;
            using var loader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), fileSystem);
            var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);
            var resolver = new CSharpWorkspaceTargetResolver(workspace, fileSystem);
            var extractor = new CSharpLanguageExtractor(loader, engine, workspace, resolver);
            await using var database = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
            var repository = new KnowledgeGraphRepository(database.CreateDbContextFactory<SharpSenseDbContext>());
            var index = new IndexTargetCommandHandler([extractor], embeddings, repository, paths, options);
            var update = new UpdateWorkspaceFilesCommandHandler([extractor], embeddings, repository, paths,
                new Mock<IWorkspaceFileDiscoverer>(MockBehavior.Strict).Object, options);

            var initial = await index.Handle(new IndexTargetCommand(), ct);
            Assert.True(initial.IsSuccess, Errors(initial));
            var callerId = await ConsumerId();

            await Write("App/Api.cs", "namespace Fixture; public static class Api { public static int Read(long value) => 2; }");
            var signatureUpdate = await update.Handle(new UpdateWorkspaceFilesCommand(
                [new(WorkspaceFileChangeAction.Modified, NewPath: Path.Combine(root, "App/Api.cs"))]), ct);
            Assert.True(signatureUpdate.IsSuccess, Errors(signatureUpdate));
            Assert.Equal(callerId, await ConsumerId());
            var afterSignature = await repository.GetPersistedCodeNodes(ct);
            Assert.Contains(afterSignature, node => node.FullyQualifiedName.Contains("Read(long)", StringComparison.Ordinal));
            Assert.DoesNotContain(afterSignature, node => node.FullyQualifiedName.Contains("Read(int)", StringComparison.Ordinal));
            await AssertMatchesFreshIndex();
            await using (var context = await database.GetContext<SharpSenseDbContext>(ct))
            {
                var targets = await (from edge in context.DependencyEdges
                    join node in context.CodeNodes on edge.CalleeNodeId equals node.Id
                    where edge.CallerNodeId == callerId && edge.EdgeType == EdgeType.MethodCall
                    select node.FullyQualifiedName).ToArrayAsync(ct);
                Assert.Contains(targets, name => name.Contains("Read(long)", StringComparison.Ordinal));
            }

            await Write("App/App.csproj", Project("<Compile Remove=\"Excluded.cs\" />"));
            File.Delete(Path.Combine(root, "App/Deleted.cs"));
            File.Delete(Path.Combine(root, "App/PartA.cs"));
            var membershipUpdate = await update.Handle(new UpdateWorkspaceFilesCommand(
            [
                new(WorkspaceFileChangeAction.Modified, NewPath: Path.Combine(root, "App/App.csproj")),
                new(WorkspaceFileChangeAction.Deleted, OldPath: Path.Combine(root, "App/Deleted.cs")),
                new(WorkspaceFileChangeAction.Deleted, OldPath: Path.Combine(root, "App/PartA.cs"))
            ]), ct);
            Assert.True(membershipUpdate.IsSuccess, Errors(membershipUpdate));
            var afterMembership = await repository.GetPersistedCodeNodes(ct);
            Assert.DoesNotContain(afterMembership, node => node.RelativeFilePath is "App/Excluded.cs" or "App/Deleted.cs" or "App/PartA.cs");
            Assert.Contains(afterMembership, node => node.FullyQualifiedName == "Fixture.Shared" && node.RelativeFilePath == "App/PartB.cs");
            await AssertMatchesFreshIndex();

            await Write("Workspace.slnx", Solution(includeOther: false));
            var projectUpdate = await update.Handle(new UpdateWorkspaceFilesCommand(
                [new(WorkspaceFileChangeAction.Modified, NewPath: target)]), ct);
            Assert.True(projectUpdate.IsSuccess, Errors(projectUpdate));
            Assert.DoesNotContain(await repository.GetPersistedCodeNodes(ct),
                node => node.FullyQualifiedName.Contains("RemovedProjectType", StringComparison.Ordinal));
            await using (var context = await database.GetContext<SharpSenseDbContext>(ct))
                Assert.Equal(1, await context.ProjectNodes.CountAsync(ct));
            await AssertMatchesFreshIndex();

            var savedSnapshot = await Snapshot(database, repository);
            await Write("Workspace.slnx", "<not-valid-xml");
            var failedUpdate = await update.Handle(new UpdateWorkspaceFilesCommand(
                [new(WorkspaceFileChangeAction.Modified, NewPath: target)]), ct);
            Assert.True(failedUpdate.IsFailed);
            Assert.Equal(savedSnapshot, await Snapshot(database, repository));

            async Task<int> ConsumerId()
            {
                await using var context = await database.GetContext<SharpSenseDbContext>(ct);
                return await context.CodeNodes.Where(node => node.FullyQualifiedName == "Fixture.Consumer.Call()")
                    .Select(node => node.Id).SingleAsync(ct);
            }

            async Task AssertMatchesFreshIndex()
            {
                using var freshLoader = new WorkspaceLoader(new MsBuildWorkspaceFactory(), fileSystem);
                var freshExtractor = new CSharpLanguageExtractor(freshLoader, engine, workspace, resolver);
                await using var freshDatabase = new InMemoryContextFactory(new(UseMigrations: true, LoadVectorExtension: true));
                var freshRepository = new KnowledgeGraphRepository(freshDatabase.CreateDbContextFactory<SharpSenseDbContext>());
                var freshIndex = new IndexTargetCommandHandler([freshExtractor], embeddings, freshRepository, paths, options);
                var result = await freshIndex.Handle(new IndexTargetCommand(), ct);
                Assert.True(result.IsSuccess, Errors(result));
                Assert.Equal(await Snapshot(freshDatabase, freshRepository), await Snapshot(database, repository));
            }

            async Task<string> Snapshot(InMemoryContextFactory factory, KnowledgeGraphRepository graphRepository)
            {
                var nodes = await graphRepository.GetPersistedCodeNodes(ct);
                await using var context = await factory.GetContext<SharpSenseDbContext>(ct);
                var edges = await (from edge in context.DependencyEdges
                    join caller in context.GraphNodes on edge.CallerNodeId equals caller.Id
                    join callee in context.GraphNodes on edge.CalleeNodeId equals callee.Id
                    select new { Caller = caller.CanonicalId, Callee = callee.CanonicalId, edge.EdgeType, edge.Metadata })
                    .ToArrayAsync(ct);
                var projects = await (from project in context.ProjectNodes
                    join graph in context.GraphNodes on project.Id equals graph.Id
                    join document in context.Documents on project.ProjectDocumentId equals document.Id
                    select new { graph.CanonicalId, project.Name, document.RelativePath, project.ContentHash })
                    .ToArrayAsync(ct);
                return JsonSerializer.Serialize(new
                {
                    Nodes = nodes.OrderBy(node => node.CanonicalId, StringComparer.Ordinal),
                    Edges = edges.OrderBy(edge => edge.Caller, StringComparer.Ordinal)
                        .ThenBy(edge => edge.Callee, StringComparer.Ordinal).ThenBy(edge => edge.EdgeType),
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
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string Project(string items = "")
        => $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{items}</ItemGroup></Project>""";

    private static string Solution(bool includeOther)
        => "<Solution><Project Path=\"App/App.csproj\" />" +
           (includeOther ? "<Project Path=\"Other/Other.csproj\" />" : "") + "</Solution>";

    private static string Errors(FluentResults.ResultBase result)
        => string.Join("; ", result.Errors.Select(error => error.Message));
}
