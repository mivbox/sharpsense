using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.CodeAnalysis;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Storage;
using System.Diagnostics;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

[Collection("MSBuild workspace")]
public sealed class RoslynTargetAnalysisEngineTests
{
    [Fact]
    public async Task WhenExtractingWithBlankPath_ThenThrowsArgumentException()
    {
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(Environment.CurrentDirectory);

        await Assert.ThrowsAsync<ArgumentException>(() => engine.Extract(string.Empty, workspace, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhenExtractingWithDisposedEngine_ThenThrowsObjectDisposedException()
    {
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(Environment.CurrentDirectory);
        engine.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.Extract("SharpSense.sln", workspace, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WhenExtractingFixtureTarget_ThenEmitsProjectsCodeNodesAndEdges()
    {
        var repositoryRoot = GetRepositoryRoot();
        var targetPath = GetFixturePath("CommandPipelineFixture.sln");
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);

        var payload = await engine.Extract(targetPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);
        var fullyQualifiedNamesById = payload.CodeNodes.ToDictionary(static codeNode => codeNode.CanonicalId, static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal);

        var appProject = Assert.Single(payload.Projects, static project => project.Name == "CommandPipelineFixture.App");
        var messageEnvelopeNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.Contracts.MessageEnvelope");
        var messageNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.Contracts.Message");
        var messageModelNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageModel");
        Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.ServiceRegistrationExtensions.AddMessagePipeline(IServiceCollection)");
        var formatterFieldNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageProvider.formatter");
        var formatterTypeNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageFormatter");

        Assert.Equal(targetPath, payload.TargetPath);
        Assert.Equal(2, payload.Projects.Count);
        Assert.Equal("tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/CommandPipelineFixture.App/CommandPipelineFixture.App.csproj", appProject.RelativeFilePath);
        Assert.NotEmpty(appProject.ContentHash);
        Assert.Contains(payload.CodeNodes, codeNode => codeNode.NodeType == NodeType.Interface);
        Assert.Contains(payload.CodeNodes, codeNode => codeNode.NodeType == NodeType.Class);
        Assert.Contains(payload.CodeNodes, codeNode => codeNode.NodeType == NodeType.Method);
        Assert.Contains(payload.CodeNodes, codeNode => codeNode.NodeType == NodeType.Property);
        Assert.Contains(payload.CodeNodes, codeNode => codeNode.NodeType == NodeType.Field);
        Assert.Equal(NodeType.Class, messageEnvelopeNode.NodeType);
        Assert.Equal(NodeType.Class, messageNode.NodeType);
        Assert.Equal(NodeType.Class, messageModelNode.NodeType);
        Assert.Contains(
            payload.Edges,
            edge => edge.CallerId == "project:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/CommandPipelineFixture.App/CommandPipelineFixture.App.csproj"
                && edge.CalleeId == "project:tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture/CommandPipelineFixture.Contracts/CommandPipelineFixture.Contracts.csproj"
                && edge.EdgeType == EdgeType.ProjectReference);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageConsumer",
            "CommandPipelineFixture.Contracts.IMessageProvider",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ConstructorInjectedConsumer",
            "CommandPipelineFixture.Contracts.IMessageProvider",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageProvider",
            "CommandPipelineFixture.Contracts.IMessageProvider",
            EdgeType.Implements);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ConstructorInjectedConsumer.Render()",
            "CommandPipelineFixture.Contracts.IMessageProvider.GetMessage()",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageProvider.GetMessage()",
            "CommandPipelineFixture.Contracts.IMessageProvider.GetMessage()",
            EdgeType.Implements);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageProvider.Name",
            "CommandPipelineFixture.Contracts.IMessageProvider.Name",
            EdgeType.Implements);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageProvider.GetMessage()",
            "CommandPipelineFixture.App.MessageFormatter.Format(string)",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageConsumer.Render()",
            "CommandPipelineFixture.Contracts.IMessageProvider.GetMessage()",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageConsumer.RenderEnvelope()",
            "CommandPipelineFixture.Contracts.IMessageProvider.GetMessage()",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageConsumer.RenderEnvelope()",
            "CommandPipelineFixture.Contracts.MessageEnvelope",
            EdgeType.Instantiates);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageConsumer.MapMessage(CommandPipelineFixture.App.MessageModel)",
            "CommandPipelineFixture.App.MessageMapper.ToMessage(CommandPipelineFixture.App.MessageModel)",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.MessageMapper.ToMessage(CommandPipelineFixture.App.MessageModel)",
            "CommandPipelineFixture.Contracts.Message",
            EdgeType.Instantiates);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ServiceRegistrationExtensions.AddMessagePipeline(IServiceCollection)",
            "CommandPipelineFixture.Contracts.IMessageProvider",
            EdgeType.ServiceRegistration);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ServiceRegistrationExtensions.AddMessagePipeline(IServiceCollection)",
            "CommandPipelineFixture.App.MessageProvider",
            EdgeType.ServiceRegistration);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ServiceRegistrationExtensions.AddMessagePipeline(IServiceCollection)",
            "CommandPipelineFixture.App.MessageConsumer",
            EdgeType.ServiceRegistration);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ServiceRegistrationExtensions.AddMessagePipeline(IServiceCollection)",
            "CommandPipelineFixture.App.MessageFormatter",
            EdgeType.ServiceRegistration);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ServiceRegistrationExtensions.AddMessagePipeline(IServiceCollection)",
            "CommandPipelineFixture.App.InferredRegistrationMarker",
            EdgeType.ServiceRegistration);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ServiceLocatorConsumer.ResolveProvider()",
            "CommandPipelineFixture.Contracts.IMessageProvider",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "CommandPipelineFixture.App.ServiceLocatorConsumer.ResolveConsumer()",
            "CommandPipelineFixture.App.MessageConsumer",
            EdgeType.Instantiates);
        Assert.Contains(
            payload.Edges,
            edge => edge.CallerId == formatterFieldNode.CanonicalId
                && edge.CalleeId == formatterTypeNode.CanonicalId
                && edge.EdgeType == EdgeType.Instantiates);
        Assert.DoesNotContain(payload.CodeNodes, static codeNode => codeNode.CanonicalId.Contains("<invalid-global-code>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenExtractingProjectTarget_ThenReturnsProjectBackedSolution()
    {
        var projectPath = GetFixturePath("CommandPipelineFixture.App/CommandPipelineFixture.App.csproj");
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(GetRepositoryRoot());

        var payload = await engine.Extract(projectPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);

        Assert.Equal(projectPath, payload.TargetPath);
        Assert.NotEmpty(payload.Projects);
        Assert.Contains(
            payload.Projects,
            static project => project.RelativeFilePath.EndsWith("CommandPipelineFixture.App/CommandPipelineFixture.App.csproj", StringComparison.Ordinal));
        Assert.NotEmpty(payload.CodeNodes);
    }

    [Fact]
    public async Task WhenExtractingFixtureTarget_ThenReportsProgressForEachProject()
    {
        var targetPath = GetFixturePath("CommandPipelineFixture.sln");
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(GetRepositoryRoot());
        var progress = new RecordingProgress();

        await engine.Extract(targetPath, workspace, new RoslynWorkspaceOptions(), progress, TestContext.Current.CancellationToken);

        Assert.Collection(
            progress.Reports,
            report =>
            {
                Assert.Equal("Parsing CommandPipelineFixture.App...", report.CurrentTask);
                Assert.Equal(1, report.CompletedItems);
                Assert.Equal(2, report.TotalItems);
            },
            report =>
            {
                Assert.Equal("Parsing CommandPipelineFixture.Contracts...", report.CurrentTask);
                Assert.Equal(2, report.CompletedItems);
                Assert.Equal(2, report.TotalItems);
            });
    }

    [Fact]
    public async Task WhenExtractingFixtureTarget_ThenEmitsSharpSenseTraceActivities()
    {
        var targetPath = GetFixturePath("CommandPipelineFixture.sln");
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(GetRepositoryRoot());
        var activityNames = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == SharpSenseTraceSpan.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activityNames.Add(activity.OperationName)
        };

        ActivitySource.AddActivityListener(listener);

        await engine.Extract(targetPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);

        Assert.Contains("roslyn.extract", activityNames);
        Assert.Contains("roslyn.open-target", activityNames);
        Assert.Contains("roslyn.build-project-nodes", activityNames);
        Assert.Contains("roslyn.build-code-nodes", activityNames);
        Assert.Contains("roslyn.build-dependency-edges", activityNames);
        Assert.Equal(2, activityNames.Count(name => name == "roslyn.parse-project"));
    }

    [Fact]
    public async Task WhenExtractingIncrementalModifiedDocument_ThenReturnsDeltaNodesAndEdges()
    {
        var fixtureRoot = CreateMutableFixtureWorkspace();
        var solutionPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.sln");
        var messageProviderPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.App", "MessageProvider.cs");
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(fixtureRoot);

        try
        {
            var fullPayload = await engine.Extract(solutionPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);
            var formatterMethodId = Assert.Single(
                fullPayload.CodeNodes,
                static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageFormatter.Format(string)").CanonicalId;
            var originalSource = await File.ReadAllTextAsync(messageProviderPath, TestContext.Current.CancellationToken);
            var updatedSource = InsertBeforeFinalBrace(
                originalSource,
                """
                    public string GetFormattedMessage()
                    {
                        return formatter.Format("Incremental");
                    }

                """);
            await File.WriteAllTextAsync(messageProviderPath, updatedSource, TestContext.Current.CancellationToken);

            var payload = await engine.ExtractIncremental(
                solutionPath,
                workspace,
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: messageProviderPath)
                ],
                ct: TestContext.Current.CancellationToken);
            var newMethod = Assert.Single(
                payload.CodeNodes,
                static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageProvider.GetFormattedMessage()");

            Assert.Empty(payload.Projects);
            Assert.All(
                payload.CodeNodes,
                static codeNode => Assert.Equal("CommandPipelineFixture.App/MessageProvider.cs", codeNode.RelativeFilePath));
            Assert.Contains(
                payload.Edges,
                edge => edge.CallerId == newMethod.CanonicalId &&
                        edge.CalleeId == formatterMethodId &&
                        edge.EdgeType == EdgeType.MethodCall);
        }
        finally
        {
            DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [Fact]
    public async Task WhenExtractingIncrementalAddedDocument_ThenReloadsWorkspaceAndReturnsNewNodes()
    {
        var fixtureRoot = CreateMutableFixtureWorkspace();
        var solutionPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.sln");
        var addedFilePath = Path.Combine(fixtureRoot, "CommandPipelineFixture.App", "IncrementalMessage.cs");
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(fixtureRoot);

        try
        {
            await engine.Extract(solutionPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(
                addedFilePath,
                """
                namespace CommandPipelineFixture.App;

                public sealed class IncrementalMessage
                {
                    public string Value { get; } = "watch";
                }
                """,
                TestContext.Current.CancellationToken);

            var payload = await engine.ExtractIncremental(
                solutionPath,
                workspace,
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Added,
                        NewPath: addedFilePath)
                ],
                ct: TestContext.Current.CancellationToken);

            Assert.Contains(
                payload.CodeNodes,
                static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.IncrementalMessage");
            Assert.All(
                payload.CodeNodes,
                static codeNode => Assert.Equal("CommandPipelineFixture.App/IncrementalMessage.cs", codeNode.RelativeFilePath));
        }
        finally
        {
            DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [Fact]
    public async Task WhenExtractingAfterIncrementalModifiedDocument_ThenReturnsUpdatedFullGraph()
    {
        var fixtureRoot = CreateMutableFixtureWorkspace();
        var solutionPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.sln");
        var messageProviderPath = Path.Combine(fixtureRoot, "CommandPipelineFixture.App", "MessageProvider.cs");
        var engine = new RoslynTargetAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(fixtureRoot);

        try
        {
            await engine.Extract(solutionPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);
            var originalSource = await File.ReadAllTextAsync(messageProviderPath, TestContext.Current.CancellationToken);
            var updatedSource = InsertBeforeFinalBrace(
                originalSource,
                """
                    public string GetFormattedMessage()
                    {
                        return formatter.Format("Incremental");
                    }

                """);
            await File.WriteAllTextAsync(messageProviderPath, updatedSource, TestContext.Current.CancellationToken);

            await engine.ExtractIncremental(
                solutionPath,
                workspace,
                [
                    new WorkspaceFileChange(
                        WorkspaceFileChangeAction.Modified,
                        NewPath: messageProviderPath)
                ],
                ct: TestContext.Current.CancellationToken);

            var payload = await engine.Extract(solutionPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);

            Assert.Contains(
                payload.CodeNodes,
                static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageProvider.GetFormattedMessage()");
        }
        finally
        {
            DeleteDirectoryIfExists(fixtureRoot);
        }
    }

    [Fact]
    public void WhenCreatingKnowledgeGraphExtractionPayload_ThenExposesDomainCollections()
    {
        var payload = new KnowledgeGraphExtractionPayload(
            "SharpSense.sln",
            [new ProjectNode { Id = "project-1", Name = "SharpSense.Domain", RelativeFilePath = "src/SharpSense.Domain/SharpSense.Domain.csproj", ContentHash = "hash-1" }],
            [new CodeNode { Id = 1, CanonicalId = "node-1", ProjectId = "project-1", FullyQualifiedName = "SharpSense.Domain.KnowledgeGraph.ProjectNode", DisplayName = "ProjectNode", NodeType = NodeType.Class, RelativeFilePath = "src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs", Summary = "Project node." }],
            [new DependencyEdge { CallerId = "node-1", CalleeId = "node-2", EdgeType = EdgeType.Implements }],
            ["warning"]);

        Assert.Equal("SharpSense.sln", payload.TargetPath);
        Assert.Single(payload.Projects);
        Assert.Single(payload.CodeNodes);
        Assert.Single(payload.Edges);
        Assert.Single(payload.Diagnostics);
    }

    private static void AssertContainsEdge(
        IReadOnlyCollection<DependencyEdge> edges,
        IReadOnlyDictionary<string, string> fullyQualifiedNamesById,
        string callerFullyQualifiedName,
        string calleeFullyQualifiedName,
        EdgeType edgeType)
        => Assert.Contains(
            edges,
            edge => edge.EdgeType == edgeType
                && fullyQualifiedNamesById.GetValueOrDefault(edge.CallerId) == callerFullyQualifiedName
                && fullyQualifiedNamesById.GetValueOrDefault(edge.CalleeId) == calleeFullyQualifiedName);

    private static string GetFixturePath(string relativePath)
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../tests/SharpSense.IntegrationTests/Assets/CommandPipelineFixture",
            relativePath));

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private static string CreateMutableFixtureWorkspace()
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), $"sharp-sense-roslyn-{Guid.NewGuid():N}");
        CopyDirectory(GetFixturePath(string.Empty), fixtureRoot);
        Directory.CreateDirectory(Path.Combine(fixtureRoot, ".git"));
        return fixtureRoot;
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(destinationPath, relativeDirectory));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relativeFile = Path.GetRelativePath(sourcePath, file);
            var destinationFile = Path.Combine(destinationPath, relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static string InsertBeforeFinalBrace(string source, string text)
    {
        var closingBraceIndex = source.LastIndexOf('}');
        return closingBraceIndex < 0
            ? source + text
            : source.Insert(closingBraceIndex, text);
    }

    private sealed class RecordingProgress : IProgress<IndexingProgress>
    {
        public List<IndexingProgress> Reports { get; } = [];

        public void Report(IndexingProgress value)
            => Reports.Add(value);
    }
}
