// using Microsoft.EntityFrameworkCore;
// using Microsoft.EntityFrameworkCore.Diagnostics;
// using Moq;
// using SharpSense.Application.Features.Indexing.IndexSolution;
// using SharpSense.Application.Shared.Models;
// using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
// using SharpSense.Infrastructure.Embeddings;
// using SharpSense.Infrastructure.Indexing;
// using SharpSense.Infrastructure.Persistence;
// using SharpSense.Infrastructure.Persistence.Initialization;
// using SharpSense.Infrastructure.Storage;
//
// namespace SharpSense.IntegrationTests;
//
// [Collection("MSBuild workspace")]
// public sealed class AnalyzeCommandIntegrationTests
// {
//     [Fact]
//     public async Task WhenIndexAsyncWithFixtureSolution_ThenPopulatesRepositoryDatabaseWithRelativePaths()
//     {
//         var repositoryRoot = CopyFixtureToTemporaryRepository();
//         var solutionPath = Path.Combine(repositoryRoot, "CommandPipelineFixture.sln");
//         var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);
//         var dbContextFactory = new TestDbContextFactory(workspace.DatabasePath, new SqlitePragmaInterceptor());
//         var embeddingsService = CreateEmbeddingsService();
//         var indexingService = new KnowledgeGraphIndexingService(
//             new RoslynSolutionAnalysisEngine(),
//             embeddingsService.Object,
//             dbContextFactory,
//             new SqliteKnowledgeGraphInitializer(),
//             workspace);
//
//         DeleteSqliteFiles(workspace.DatabasePath);
//
//         try
//         {
//             var summary = await indexingService.Index(
//                 new IndexSolutionCommand(solutionPath),
//                 TestContext.Current.CancellationToken);
//
//             Assert.Equal(solutionPath, summary.SolutionPath);
//             Assert.Equal(2, summary.ProjectCount);
//             Assert.True(summary.CodeNodeCount >= 10);
//             Assert.True(summary.DependencyCount >= 4);
//
//             await using var dbContext = await dbContextFactory.CreateDbContextAsync(TestContext.Current.CancellationToken);
//             var projectNodes = await dbContext.ProjectNodes
//                 .AsNoTracking()
//                 .OrderBy(static project => project.Name)
//                 .ToArrayAsync(TestContext.Current.CancellationToken);
//             var codeNodes = await dbContext.CodeNodes
//                 .AsNoTracking()
//                 .OrderBy(static codeNode => codeNode.FullyQualifiedName)
//                 .ToArrayAsync(TestContext.Current.CancellationToken);
//             var dependencyEdges = await dbContext.DependencyEdges
//                 .AsNoTracking()
//                 .ToArrayAsync(TestContext.Current.CancellationToken);
//
//             Assert.Equal(2, projectNodes.Length);
//             Assert.True(codeNodes.Length >= 10);
//             Assert.True(dependencyEdges.Length >= 4);
//             Assert.All(projectNodes, static project => Assert.False(Path.IsPathRooted(project.RelativeFilePath)));
//             Assert.All(codeNodes, static codeNode => Assert.False(Path.IsPathRooted(codeNode.RelativeFilePath)));
//         }
//         finally
//         {
//             DeleteSqliteFiles(workspace.DatabasePath);
//             DeleteDirectoryIfExists(repositoryRoot);
//         }
//     }
//
//     private static string CopyFixtureToTemporaryRepository()
//     {
//         var sourceRoot = GetRepositoryPath("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture");
//         var destinationRoot = Path.Combine(AppContext.BaseDirectory, $"SharpSense.IntegrationTests.{Guid.NewGuid():N}");
//
//         CopyDirectory(sourceRoot, destinationRoot);
//         Directory.CreateDirectory(Path.Combine(destinationRoot, ".git"));
//         return destinationRoot;
//     }
//
//     private static void CopyDirectory(string sourceRoot, string destinationRoot)
//     {
//         Directory.CreateDirectory(destinationRoot);
//
//         foreach (var directory in Directory.GetDirectories(sourceRoot))
//         {
//             var directoryName = Path.GetFileName(directory);
//             if (directoryName is "bin" or "obj")
//             {
//                 continue;
//             }
//
//             CopyDirectory(directory, Path.Combine(destinationRoot, directoryName));
//         }
//
//         foreach (var file in Directory.GetFiles(sourceRoot))
//         {
//             File.Copy(file, Path.Combine(destinationRoot, Path.GetFileName(file)));
//         }
//     }
//
//     private static void DeleteDirectoryIfExists(string path)
//     {
//         if (Directory.Exists(path))
//         {
//             Directory.Delete(path, recursive: true);
//         }
//     }
//
//     private static void DeleteSqliteFiles(string path)
//     {
//         foreach (var sqlitePath in new[] { path, $"{path}-wal", $"{path}-shm" })
//         {
//             if (File.Exists(sqlitePath))
//             {
//                 File.Delete(sqlitePath);
//             }
//         }
//     }
//
//     private static string GetRepositoryPath(string relativePath)
//         => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));
//
//     private static Mock<IEmbeddingGenerator> CreateEmbeddingsService()
//     {
//         var embeddingsService = new Mock<IEmbeddingGenerator>();
//         embeddingsService
//             .Setup(service => service.GenerateBatch(
//                 It.IsAny<IEnumerable<string>>(),
//                 It.IsAny<IProgress<EmbeddingGenerationProgress>?>(),
//                 It.IsAny<CancellationToken>()))
//             .Returns(
//                 static (IEnumerable<string> texts, IProgress<EmbeddingGenerationProgress>? progress, CancellationToken ct) =>
//                 {
//                     var normalizedTexts = texts.ToArray();
//                     progress?.Report(new EmbeddingGenerationProgress("Generating embeddings...", 0, normalizedTexts.Length));
//
//                     var embeddings = new List<TextEmbedding>(normalizedTexts.Length);
//                     for (var index = 0; index < normalizedTexts.Length; index++)
//                     {
//                         ct.ThrowIfCancellationRequested();
//                         var text = normalizedTexts[index];
//                         embeddings.Add(new TextEmbedding(text, CreateVector(text)));
//                         progress?.Report(new EmbeddingGenerationProgress("Generating embeddings...", index + 1, normalizedTexts.Length));
//                     }
//
//                     return Task.FromResult<IReadOnlyList<TextEmbedding>>(embeddings);
//                 });
//
//         return embeddingsService;
//
//         static float[] CreateVector(string text)
//             => [text.Length, text.Count(static character => character == ' '), text.Count(char.IsLetter)];
//     }
//
//     private sealed class TestDbContextFactory(
//         string databasePath,
//         params IInterceptor[] interceptors)
//         : IDbContextFactory<SharpSenseDbContext>
//     {
//         public SharpSenseDbContext CreateDbContext()
//         {
//             var options = new DbContextOptionsBuilder<SharpSenseDbContext>()
//                 .UseSqlite($"Data Source={databasePath};Mode=ReadWriteCreate;Cache=Shared")
//                 .AddInterceptors(interceptors)
//                 .Options;
//
//             return new SharpSenseDbContext(options);
//         }
//
//         public Task<SharpSenseDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
//             => Task.FromResult(CreateDbContext());
//
//     }
// }
