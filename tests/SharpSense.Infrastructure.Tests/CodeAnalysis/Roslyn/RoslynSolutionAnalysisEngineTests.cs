using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Application.Shared.Models;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.CodeAnalysis;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Storage;
using System.Diagnostics;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

public sealed class RoslynSolutionAnalysisEngineTests
{
    [Fact]
    public async Task ExtractAsync_throws_for_blank_paths()
    {
        var engine = new RoslynSolutionAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(Environment.CurrentDirectory);

        await Assert.ThrowsAsync<ArgumentException>(() => engine.ExtractAsync(string.Empty, workspace, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExtractAsync_loads_fixture_solution_and_emits_projects_code_nodes_and_edges()
    {
        var repositoryRoot = GetRepositoryRoot();
        var solutionPath = GetFixturePath("CommandPipelineFixture.sln");
        var engine = new RoslynSolutionAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(repositoryRoot);

        var payload = await engine.ExtractAsync(solutionPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);
        var fullyQualifiedNamesById = payload.CodeNodes.ToDictionary(static codeNode => codeNode.Id, static codeNode => codeNode.FullyQualifiedName, StringComparer.Ordinal);

        var appProject = Assert.Single(payload.Projects, static project => project.Name == "CommandPipelineFixture.App");
        var messageEnvelopeNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.Contracts.MessageEnvelope");
        var messageNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.Contracts.Message");
        var messageModelNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageModel");
        Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.ServiceRegistrationExtensions.AddMessagePipeline(IServiceCollection)");
        var formatterFieldNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageProvider.formatter");
        var formatterTypeNode = Assert.Single(payload.CodeNodes, static codeNode => codeNode.FullyQualifiedName == "CommandPipelineFixture.App.MessageFormatter");

        Assert.Equal(solutionPath, payload.SolutionPath);
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
            edge => edge.CallerId == formatterFieldNode.Id
                && edge.CalleeId == formatterTypeNode.Id
                && edge.EdgeType == EdgeType.Instantiates);
        Assert.DoesNotContain(payload.CodeNodes, static codeNode => codeNode.Id.Contains("<invalid-global-code>", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractAsync_reports_progress_for_each_project()
    {
        var solutionPath = GetFixturePath("CommandPipelineFixture.sln");
        var engine = new RoslynSolutionAnalysisEngine();
        var workspace = RepositoryWorkspace.CreateFromWorkingDirectory(GetRepositoryRoot());
        var progress = new RecordingProgress();

        await engine.ExtractAsync(solutionPath, workspace, new RoslynWorkspaceOptions(), progress, TestContext.Current.CancellationToken);

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
    public async Task ExtractAsync_emits_sharpsense_trace_activities()
    {
        var solutionPath = GetFixturePath("CommandPipelineFixture.sln");
        var engine = new RoslynSolutionAnalysisEngine();
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

        await engine.ExtractAsync(solutionPath, workspace, new RoslynWorkspaceOptions(), ct: TestContext.Current.CancellationToken);

        Assert.Contains("roslyn.extract", activityNames);
        Assert.Contains("roslyn.open-solution", activityNames);
        Assert.Contains("roslyn.build-project-nodes", activityNames);
        Assert.Contains("roslyn.build-code-nodes", activityNames);
        Assert.Contains("roslyn.build-dependency-edges", activityNames);
        Assert.Equal(2, activityNames.Count(name => name == "roslyn.parse-project"));
    }

    [Fact]
    public void KnowledgeGraphExtractionPayload_exposes_domain_collections()
    {
        var payload = new KnowledgeGraphExtractionPayload(
            "SharpSense.sln",
            [new ProjectNode { Id = "project-1", Name = "SharpSense.Domain", RelativeFilePath = "src/SharpSense.Domain/SharpSense.Domain.csproj", ContentHash = "hash-1" }],
            [new CodeNode { Id = "node-1", ProjectId = "project-1", FullyQualifiedName = "SharpSense.Domain.KnowledgeGraph.ProjectNode", NodeType = NodeType.Class, RelativeFilePath = "src/SharpSense.Domain/KnowledgeGraph/Nodes/ProjectNode.cs", Summary = "Project node." }],
            [new DependencyEdge { CallerId = "node-1", CalleeId = "node-2", EdgeType = EdgeType.Implements }],
            ["warning"]);

        Assert.Equal("SharpSense.sln", payload.SolutionPath);
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

    private sealed class RecordingProgress : IProgress<IndexingProgress>
    {
        public List<IndexingProgress> Reports { get; } = [];

        public void Report(IndexingProgress value)
            => Reports.Add(value);
    }
}
