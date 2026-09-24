using System.Diagnostics;
using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Options;
using Moq;
using SharpSense.Application.Indexing;
using SharpSense.Application.Indexing.Abstractions;
using SharpSense.Application.Indexing.IndexTarget;
using SharpSense.Application.Indexing.IndexTarget.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Abstractions;
using SharpSense.Application.Shared.Options;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing.CSharp;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.Markdown;
using SharpSense.Infrastructure.Storage;

namespace SharpSense.Infrastructure.Tests.Indexing.CSharp;

public sealed class CSharpWorkspaceReuseTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DocumentationFastPathSkipsRealSdkProjectButRefreshesDeclaredAndNewGeneratorInputs()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "sharpsense-docs-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        Directory.CreateDirectory(Path.Combine(root, "inputs"));
        try
        {
            CreateGenerator(Path.Combine(root, "FixtureGenerator.dll"));
            await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), Project(
                "<Analyzer Include=\"FixtureGenerator.dll\" /><AdditionalFiles Include=\"inputs/*.md\" />"), ct);
            await File.WriteAllTextAsync(Path.Combine(root, "Api.cs"), Source("int", 0), ct);
            var guide = Path.Combine(root, "docs/guide.md");
            var schema = Path.Combine(root, "inputs/schema.md");
            await File.WriteAllTextAsync(guide, "# Guide\nOriginal documentation.", ct);
            await File.WriteAllTextAsync(schema, "0", ct);

            var fileSystem = new FileSystem();
            var workspace = new RepositoryWorkspace(root, Path.Combine(root, "unused.db"), fileSystem);
            var factory = new CountingWorkspaceFactory();
            using var loader = new WorkspaceLoader(factory, fileSystem);
            var csharp = new CSharpLanguageExtractor(loader,
                new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem), workspace,
                new CSharpWorkspaceTargetResolver(workspace, fileSystem));
            var configuration = new Mock<IOptionsMonitor<SharpSenseConfig>>(MockBehavior.Strict);
            var markdown = new MarkdownDocumentExtractor(new DocumentDiscoverer(workspace, configuration.Object,
                new WorkspaceFileDiscoverer(workspace, fileSystem), new MarkdownIndexer(), fileSystem));
            ILanguageExtractor[] extractors = [csharp, markdown];
            var paths = new IndexingWorkspacePaths(workspace);
            using var coordinator = new WorkspaceExtractionCoordinator(extractors, paths);
            var options = Options.Create(new SharpSenseCliOptions
            {
                WorkspaceId = "fixture",
                WorkspaceSources = [new(WorkspaceSourceKind.CSharp, "App.csproj"), new(WorkspaceSourceKind.Markdown, "docs/**/*.md")],
                SkipEmbeddings = true,
                DisableEmbeddingCache = true
            });
            ExtractedNodes? persisted = null;
            var repository = new Mock<IKnowledgeGraphRepository>(MockBehavior.Strict);
            repository.Setup(candidate => candidate.ReplaceTarget(It.IsAny<ExtractedNodes>(), ct))
                .Callback<ExtractedNodes, CancellationToken>((value, _) => persisted = value)
                .Returns(Task.CompletedTask);

            var watch = Stopwatch.StartNew();
            await Index();
            var initialMilliseconds = watch.Elapsed.TotalMilliseconds;
            Assert.Contains(persisted!.CodeNodes, node => node.FullyQualifiedName == "Generated.AdditionalVersion0");
            Assert.Equal(1, factory.CreateCount);

            await File.WriteAllTextAsync(guide, "# Guide\nUpdated documentation.", ct);
            watch.Restart();
            await Index(new(WorkspaceFileChangeAction.Modified, NewPath: guide));
            var docsMilliseconds = watch.Elapsed.TotalMilliseconds;
            Assert.Equal(1, factory.CreateCount);
            Assert.Contains(persisted!.CodeNodes, node => node.FullyQualifiedName == "Generated.AdditionalVersion0");
            Assert.Contains(persisted.CodeNodes, node => node.Summary.Contains("Updated documentation.", StringComparison.Ordinal));

            // Declared Markdown generator inputs refresh even though they aren't selected docs.
            await File.WriteAllTextAsync(schema, "1", ct);
            await Index(new(WorkspaceFileChangeAction.Modified, NewPath: schema));
            Assert.Equal(2, factory.CreateCount);
            Assert.Contains(persisted!.CodeNodes, node => node.FullyQualifiedName == "Generated.AdditionalVersion1");
            Assert.DoesNotContain(persisted.CodeNodes, node => node.FullyQualifiedName == "Generated.AdditionalVersion0");

            File.Delete(schema);
            await Index(new(WorkspaceFileChangeAction.Deleted, OldPath: schema));
            Assert.Equal(3, factory.CreateCount);
            Assert.DoesNotContain(persisted!.CodeNodes, node => node.FullyQualifiedName.StartsWith("Generated.AdditionalVersion", StringComparison.Ordinal));

            // New membership of an empty AdditionalFiles glob cannot be inferred from cached paths.
            await File.WriteAllTextAsync(schema, "2", ct);
            await Index(new(WorkspaceFileChangeAction.Added, NewPath: schema));
            Assert.Equal(4, factory.CreateCount);
            Assert.Contains(persisted!.CodeNodes, node => node.FullyQualifiedName == "Generated.AdditionalVersion2");
            output.WriteLine("Real SDK project with source generator: initial {0:F1} ms; documentation-only update {1:F1} ms; no additional C# workspace load.",
                initialMilliseconds, docsMilliseconds);

            async Task Index(WorkspaceFileChange? change = null)
            {
                var handler = new IndexTargetCommandHandler(extractors,
                    new Mock<IEmbeddingGenerator>(MockBehavior.Strict).Object, repository.Object, paths, options,
                    workspaceExtraction: coordinator);
                var result = await handler.Handle(new IndexTargetCommand(ChangedFiles: change is null ? null : [change]), ct);
                Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WhenGeneratorHasAdditionalFiles_ThenSourceEditReloadsUnreportedInputChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "sharpsense-csharp-inputs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateGenerator(Path.Combine(root, "FixtureGenerator.dll"));
            var target = Path.Combine(root, "App.csproj");
            var sourcePath = Path.Combine(root, "Api.cs");
            var inputPath = Path.Combine(root, "schema.txt");
            await File.WriteAllTextAsync(target, Project(
                "<Analyzer Include=\"FixtureGenerator.dll\" /><AdditionalFiles Include=\"schema.txt\" />"), ct);
            await File.WriteAllTextAsync(sourcePath, Source("int", 0), ct);
            await File.WriteAllTextAsync(inputPath, "0", ct);
            var fileSystem = new FileSystem();
            var workspace = new RepositoryWorkspace(root, Path.Combine(root, "unused.db"), fileSystem);
            var factory = new CountingWorkspaceFactory();
            using var loader = new WorkspaceLoader(factory, fileSystem);
            var extractor = new CSharpLanguageExtractor(loader,
                new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem), workspace,
                new CSharpWorkspaceTargetResolver(workspace, fileSystem));
            var initial = await extractor.Extract(new(target, null), ct);
            Assert.True(initial.IsSuccess);
            Assert.Contains(initial.Value.CodeNodes, node => node.FullyQualifiedName == "Generated.AdditionalVersion0");

            // Only the .cs event is reported; the arbitrary generator input must still refresh.
            await File.WriteAllTextAsync(inputPath, "1", ct);
            await File.WriteAllTextAsync(sourcePath, Source("int", 1), ct);
            var updated = await extractor.Extract(new(target, null,
                [new(WorkspaceFileChangeAction.Modified, NewPath: sourcePath)]), ct);

            Assert.True(updated.IsSuccess);
            Assert.Contains(updated.Value.CodeNodes, node => node.FullyQualifiedName == "Generated.AdditionalVersion1");
            Assert.DoesNotContain(updated.Value.CodeNodes, node => node.FullyQualifiedName == "Generated.AdditionalVersion0");
            Assert.Equal(2, factory.CreateCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WhenExistingSourcesChange_ThenReusesWorkspaceAndMatchesFreshDependentProjectGraph()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "sharpsense-csharp-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var generatorPath = Path.Combine(root, "FixtureGenerator.dll");
            CreateGenerator(generatorPath);
            await Write("Library/Library.csproj", Project("<Analyzer Include=\"../FixtureGenerator.dll\" />"));
            await Write("App/App.csproj", Project("<ProjectReference Include=\"../Library/Library.csproj\" />"));
            await Write("Workspace.slnx", "<Solution><Project Path=\"Library/Library.csproj\" /><Project Path=\"App/App.csproj\" /></Solution>");
            await Write("Library/Api.cs", Source("int", 0));
            await Write("Library/Part.cs", "namespace Fixture; public static partial class Api { public static int Other() => 3; }");
            for (var index = 0; index < 80; index++)
            {
                await Write($"App/Consumer{index}.cs",
                    $"namespace Fixture; public class Consumer{index} {{ public long Call() => Api.Read(1); }}");
            }

            var target = Path.Combine(root, "Workspace.slnx");
            var fileSystem = new FileSystem();
            var workspace = new RepositoryWorkspace(root, Path.Combine(root, "unused.db"), fileSystem);
            var resolver = new CSharpWorkspaceTargetResolver(workspace, fileSystem);
            var engine = new RoslynTargetAnalysisEngine(new NodeExtractor(), new EdgeExtractor(), fileSystem);
            var warmFactory = new CountingWorkspaceFactory();
            var reloadFactory = new CountingWorkspaceFactory();
            using var warmLoader = new WorkspaceLoader(warmFactory, fileSystem);
            using var reloadLoader = new WorkspaceLoader(reloadFactory, fileSystem);
            var warmExtractor = new CSharpLanguageExtractor(warmLoader, engine, workspace, resolver);
            var reloadExtractor = new CSharpLanguageExtractor(reloadLoader, engine, workspace, resolver);
            Assert.True((await warmExtractor.Extract(new(target, null), ct)).IsSuccess);
            Assert.True((await reloadExtractor.Extract(new(target, null), ct)).IsSuccess);
            var reusedTimes = new List<double>();
            var reloadTimes = new List<double>();

            for (var version = 1; version <= 3; version++)
            {
                await Write("Library/Api.cs", Source("long", version));
                var watch = Stopwatch.StartNew();
                var updated = await warmExtractor.Extract(new ExtractionContext(target, null,
                    [new(WorkspaceFileChangeAction.Modified, NewPath: "Library/Api.cs")]), ct);
                reusedTimes.Add(watch.Elapsed.TotalMilliseconds);
                watch.Restart();
                var fresh = await reloadExtractor.Extract(new(target, null), ct);
                reloadTimes.Add(watch.Elapsed.TotalMilliseconds);

                Assert.True(updated.IsSuccess, string.Join("; ", updated.Errors));
                Assert.True(fresh.IsSuccess, string.Join("; ", fresh.Errors));
                Assert.Equal(Snapshot(fresh.Value), Snapshot(updated.Value));
                Assert.Contains(updated.Value.CodeNodes, node => node.FullyQualifiedName.Contains("Read(long)", StringComparison.Ordinal));
                Assert.DoesNotContain(updated.Value.CodeNodes, node => node.FullyQualifiedName.Contains("Read(int)", StringComparison.Ordinal));
                Assert.Contains(updated.Value.CodeNodes, node => node.RelativeFilePath == "Library/Part.cs");
                var method = Assert.Single(updated.Value.CodeNodes,
                    node => node.FullyQualifiedName == "Fixture.Api.Read(long)");
                Assert.Equal(80, updated.Value.Edges.Count(edge =>
                    edge.CalleeId == method.CanonicalId && edge.EdgeType == EdgeType.MethodCall));
                Assert.Contains(updated.Value.CodeNodes, node => node.FullyQualifiedName == $"Generated.Version{version}");
                Assert.DoesNotContain(updated.Value.CodeNodes, node => node.FullyQualifiedName == $"Generated.Version{version - 1}");
            }

            Assert.Equal(1, warmFactory.CreateCount);
            Assert.Equal(4, reloadFactory.CreateCount);
            output.WriteLine("C# full extraction, 2 projects / 82 source files / 3 edits: reload median {0:F1} ms; reused workspace median {1:F1} ms.",
                reloadTimes.Order().ElementAt(1), reusedTimes.Order().ElementAt(1));

            async Task Write(string path, string contents)
            {
                var absolutePath = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
                await File.WriteAllTextAsync(absolutePath, contents, ct);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string Snapshot(ExtractedNodes nodes)
        => JsonSerializer.Serialize(new { nodes.Projects, nodes.CodeNodes, nodes.Edges });

    private static string Project(string items = "")
        => $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>{items}</ItemGroup></Project>""";

    private static string Source(string parameterType, int version)
        => $"namespace Fixture; public static partial class Api {{ public static long Read({parameterType} value) => value + {version}; }} // Generation: {version}";

    private static void CreateGenerator(string path)
    {
        const string source = """
            using Microsoft.CodeAnalysis;

            [Generator]
            public sealed class FixtureGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }

                public void Execute(GeneratorExecutionContext context)
                {
                    foreach (var tree in context.Compilation.SyntaxTrees)
                    {
                        var text = tree.ToString();
                        var marker = text.IndexOf("// Generation: ", System.StringComparison.Ordinal);
                        if (marker < 0) continue;
                        var version = text.Substring(marker + "// Generation: ".Length).Trim();
                        context.AddSource("Version.g.cs", "namespace Generated { public class Version" + version + " {} }");
                    }

                    foreach (var file in context.AdditionalFiles)
                    {
                        var version = file.GetText(context.CancellationToken).ToString().Trim();
                        context.AddSource("AdditionalVersion.g.cs", "namespace Generated { public class AdditionalVersion" + version + " {} }");
                    }
                }
            }
            """;
        var platformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = platformAssemblies
            .Where(assembly => Path.GetFileName(assembly) is "System.Private.CoreLib.dll" or "System.Runtime.dll" or "System.Collections.Immutable.dll")
            .Append(typeof(ISourceGenerator).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(assembly => MetadataReference.CreateFromFile(assembly));
        var compilation = CSharpCompilation.Create("FixtureGenerator", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    private sealed class CountingWorkspaceFactory : IMsBuildWorkspaceFactory
    {
        public int CreateCount { get; private set; }

        public MSBuildWorkspace Create()
        {
            CreateCount++;
            return new MsBuildWorkspaceFactory().Create();
        }
    }
}
