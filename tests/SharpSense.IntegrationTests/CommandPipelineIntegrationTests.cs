// using Microsoft.EntityFrameworkCore;
// using SharpSense.Application.Features.HybridSearch.Abstractions;
// using SharpSense.Application.Features.HybridSearch.Queries;
// using SharpSense.Application.Features.ImpactAnalysis.Abstractions;
// using SharpSense.Application.Features.ImpactAnalysis.Queries;
// using SharpSense.Application.Features.Indexing.Abstractions;
// using SharpSense.Application.Features.Indexing.Commands;
// using SharpSense.Domain.KnowledgeGraph.Enums;
// using SharpSense.Infrastructure.Embeddings;
// using SharpSense.Infrastructure.Persistence;
// using SharpSense.Infrastructure.Storage;
//
// namespace SharpSense.IntegrationTests;
//
// [Collection("MSBuild workspace")]
// public sealed class CommandPipelineIntegrationTests
// {
//     [Fact]
//     public async Task Index_search_and_impact_pipeline_processes_fixture_solution()
//     {
//         var solutionPath = GetFixturePath("CommandPipelineFixture.sln");
//         var repositoryRoot = Path.GetDirectoryName(solutionPath)!;
//         var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
//         var databasePath = workspace.DatabasePath;
//
//         DeleteSqliteFiles(databasePath);
//
//         var embeddingsService = new TestLocalEmbeddingsService();
//         using var provider = SharpSense.Cli.Program.CreateServiceProvider(
//             repositoryRoot,
//             configureServices: services => services.AddSingleton<ILocalEmbeddingsService>(embeddingsService));
//         var indexingService = provider.GetRequiredService<IKnowledgeGraphIndexingService>();
//         var searchService = provider.GetRequiredService<IHybridSearcher>();
//         var impactService = provider.GetRequiredService<IImpactAnalyzer>();
//         var dbContextFactory = provider.GetRequiredService<IDbContextFactory<SharpSenseDbContext>>();
//
//         try
//         {
//             var summary = await indexingService.IndexAsync(new IndexSolutionCommand(solutionPath), CancellationToken.None);
//
//             Assert.Equal(solutionPath, summary.SolutionPath);
//             Assert.Equal(2, summary.ProjectCount);
//             Assert.True(summary.CodeNodeCount >= 10);
//             Assert.True(summary.DependencyCount >= 4);
//
//             await using (var dbContext = await dbContextFactory.CreateDbContextAsync())
//             {
//                 Assert.Equal(2, await dbContext.ProjectNodes.CountAsync());
//                 Assert.True(await dbContext.CodeNodes.CountAsync() >= 10);
//                 Assert.True(await dbContext.DependencyEdges.CountAsync() >= 4);
//
//                 var messageProvider = await dbContext.CodeNodes.SingleAsync(
//                     static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageProvider");
//
//                 Assert.NotNull(messageProvider.VectorEmbedding);
//                 Assert.NotEmpty(messageProvider.VectorEmbedding!);
//             }
//
//             var searchResult = await searchService.SearchAsync(
//                 new HybridSearchQuery("CommandPipelineFixture.App.MessageProvider"),
//                 CancellationToken.None);
//             var impactResult = await impactService.AnalyzeAsync(
//                 new ImpactAnalysisQuery(
//                     "CommandPipelineFixture.Contracts.IMessageProvider",
//                     IncludedEdgeTypes: [EdgeType.Implements]),
//                 CancellationToken.None);
//
//             Assert.NotEmpty(searchResult.Hits);
//             Assert.Equal("CommandPipelineFixture.App.MessageProvider", searchResult.Hits[0].FullyQualifiedName);
//             Assert.Equal("CommandPipelineFixture.Contracts.IMessageProvider", impactResult.NodeId);
//             Assert.Contains(
//                 impactResult.ImpactedNodes,
//                 static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageProvider");
//             Assert.Contains(impactResult.Dependencies, static edge => edge.EdgeType == EdgeType.Implements);
//         }
//         finally
//         {
//             DeleteSqliteFiles(databasePath);
//         }
//     }
//
//     private static string GetFixturePath(string relativePath)
//         => Path.GetFullPath(Path.Combine(
//             AppContext.BaseDirectory,
//             "../../../../../tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture",
//             relativePath));
//
//     private static void DeleteSqliteFiles(string databasePath)
//     {
//         foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
//         {
//             if (File.Exists(path))
//             {
//                 File.Delete(path);
//             }
//         }
//     }
// }
