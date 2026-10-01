using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Storage;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.CodeAnalysis.Roslyn;

public sealed class RoslynTargetAnalysisEngineTests
{
    [Fact]
    public async Task WhenExtractingSolution_ThenEmitsProjectsCodeNodesAndEdges()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        await using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        using var fixture = CreateFixtureSolution();

        var payload = await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            fixture.Solution,
            workspace,
            ct: TestContext.Current.CancellationToken);
        var fullyQualifiedNamesById = payload.CodeNodes
            .ToDictionary(
                static codeNode => codeNode.CanonicalId,
                static codeNode => codeNode.FullyQualifiedName,
                StringComparer.Ordinal);

        var appProject = payload.Projects.Should().ContainSingle(project => project.Name == "App").Subject;
        payload.TargetPath.Should().Be("/repo/CommandPipelineFixture.sln");
        payload.Projects.Should().HaveCount(2);
        appProject.RelativeFilePath.Should().Be("App/App.csproj");
        appProject.ContentHash.Should().NotBeNullOrWhiteSpace();
        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "Contracts.IMessageProvider");
        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "App.MessageProvider");
        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "App.MessageConsumer.Render()");
        payload.CodeNodes
            .Select(node => node.CanonicalId)
            .Should().OnlyHaveUniqueItems();
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.MessageProvider",
            "Contracts.IMessageProvider",
            EdgeType.Implements);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.MessageConsumer.Render()",
            "Contracts.IMessageProvider.GetMessage()",
            EdgeType.MethodCall);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.ServiceRegistrationExtensions.AddMessagePipeline(Microsoft.Extensions.DependencyInjection.IServiceCollection)",
            "Contracts.IMessageProvider",
            EdgeType.ServiceRegistration);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.ServiceRegistrationExtensions.AddMessagePipeline(Microsoft.Extensions.DependencyInjection.IServiceCollection)",
            "App.MessageProvider",
            EdgeType.ServiceRegistration);
        payload.Edges.Should().Contain(edge =>
            edge.CallerId == "project:App/App.csproj" &&
            edge.CalleeId == "project:Contracts/Contracts.csproj" &&
            edge.EdgeType == EdgeType.ProjectReference);
    }

    [Fact]
    public async Task WhenProjectsDeclareMatchingNames_ThenKeepsTheirNodesAndCallsSeparate()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        await using var services = CreateServiceProvider(fileSystem);
        var engine = services.GetRequiredService<ITargetAnalysisEngine>();
        var solution = CreateFixtureSolution().Solution;
        const string source = """
            namespace Shared;
            public class Options
            {
                public void Save() { }
                public void Run() { Save(); }
            }
            """;

        foreach (var project in solution.Projects.ToArray())
        {
            solution = solution.AddDocument(
                DocumentId.CreateNewId(project.Id),
                "Options.cs",
                SourceText.From(source),
                filePath: $"/repo/{project.Name}/Options.cs");
        }

        var result = await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            solution,
            workspace,
            ct: TestContext.Current.CancellationToken);

        var options = result.CodeNodes
            .Where(node => node.FullyQualifiedName == "Shared.Options")
            .ToArray();
        options.Should().HaveCount(2);
        options
            .Select(node => node.ProjectId).Should().OnlyHaveUniqueItems();
        foreach (var option in options)
        {
            var caller = result.CodeNodes.Single(node =>
                node.ProjectId == option.ProjectId && node.FullyQualifiedName == "Shared.Options.Run()");
            var callee = result.CodeNodes.Single(node =>
                node.ProjectId == option.ProjectId && node.FullyQualifiedName == "Shared.Options.Save()");
            result.Edges.Should().Contain(edge => edge.EdgeType == EdgeType.MethodCall &&
                edge.CallerId == caller.CanonicalId && edge.CalleeId == callee.CanonicalId);
            result.Edges.Should().NotContain(edge => edge.EdgeType == EdgeType.MethodCall &&
                edge.CallerId == caller.CanonicalId && edge.CalleeId != callee.CanonicalId);
        }
    }

    [Fact]
    public async Task WhenExtractingProject_ThenReturnsProjectBackedPayload()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        using var fixture = CreateFixtureSolution();

        var payload = await engine.Extract(
            "/repo/App/App.csproj",
            fixture.AppProject.Solution,
            workspace,
            ct: TestContext.Current.CancellationToken);

        payload.TargetPath.Should().Be("/repo/App/App.csproj");
        payload.Projects.Should().NotBeEmpty();
        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "App.MessageProvider");
    }

    [Fact]
    public async Task WhenExtractingInvocationInsideLambdaArgument_ThenEmitsMethodCallEdgeForInnerInvocation()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        using var fixture = CreateFixtureSolution();

        var payload = await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            fixture.Solution,
            workspace,
            ct: TestContext.Current.CancellationToken);
        var fullyQualifiedNamesById = payload.CodeNodes
            .ToDictionary(
                static codeNode => codeNode.CanonicalId,
                static codeNode => codeNode.FullyQualifiedName,
                StringComparer.Ordinal);

        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.PaymentGatewayExpectationBuilder.Configure()",
            "Contracts.IPaymentGateway.ProcessPayment()",
            EdgeType.MethodCall);
    }

    [Fact]
    public async Task WhenExtractingSolution_ThenEmitsStructuralParentEdges()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        using var fixture = CreateFixtureSolution();

        var payload = await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            fixture.Solution,
            workspace,
            ct: TestContext.Current.CancellationToken);
        var fullyQualifiedNamesById = payload.CodeNodes
            .ToDictionary(
                static codeNode => codeNode.CanonicalId,
                static codeNode => codeNode.FullyQualifiedName,
                StringComparer.Ordinal);

        payload.Edges.Should().Contain(edge =>
            edge.CallerId == "project:App/App.csproj" &&
            fullyQualifiedNamesById.GetValueOrDefault(edge.CalleeId) == "App.MessageProvider" &&
            edge.EdgeType == EdgeType.ParentOf);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.MessageProvider",
            "App.MessageProvider._prefix",
            EdgeType.ParentOf);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.MessageProvider",
            "App.MessageProvider.GetMessage()",
            EdgeType.ParentOf);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.OuterProcessor",
            "App.OuterProcessor.InnerProcessor",
            EdgeType.ParentOf);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.OuterProcessor.InnerProcessor",
            "App.OuterProcessor.InnerProcessor.Execute()",
            EdgeType.ParentOf);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.PartialProcessor",
            "App.PartialProcessor.Build()",
            EdgeType.ParentOf);
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.PartialProcessor",
            "App.PartialProcessor.Format()",
            EdgeType.ParentOf);
    }

    [Fact]
    public async Task WhenExtractingModifiedSolution_ThenReturnsUpdatedNodesAndEdges()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        using var fixture = CreateFixtureSolution();
        var updatedSource = """
            using Contracts;

            namespace App;

            public sealed class MessageProvider : IMessageProvider
            {
                public string Name => "provider";

                public string GetMessage()
                {
                    return "Hello";
                }

                public string GetCopiedMessage()
                {
                    return GetMessage();
                }
            }
            """;
        var updatedSolution = fixture.Solution.WithDocumentText(
            fixture.MessageProviderDocumentId,
            SourceText.From(updatedSource));

        var payload = await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            updatedSolution,
            workspace,
            ct: TestContext.Current.CancellationToken);
        var fullyQualifiedNamesById = payload.CodeNodes
            .ToDictionary(
                static codeNode => codeNode.CanonicalId,
                static codeNode => codeNode.FullyQualifiedName,
                StringComparer.Ordinal);

        payload.Projects.Should().NotBeEmpty();
        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "App.MessageProvider.GetCopiedMessage()");
        payload.CodeNodes.Should().Contain(codeNode => codeNode.RelativeFilePath == "App/MessageProvider.cs");
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.MessageProvider.GetCopiedMessage()",
            "App.MessageProvider.GetMessage()",
            EdgeType.MethodCall);
    }

    [Fact]
    public async Task WhenExtractingSolutionWithAddedDocument_ThenReturnsNewNodes()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        using var fixture = CreateFixtureSolution();
        var incrementalMessageDocumentId = DocumentId.CreateNewId(fixture.AppProject.Id);
        var updatedSolution = fixture.Solution.AddDocument(
            incrementalMessageDocumentId,
            "IncrementalMessage.cs",
            SourceText.From(
                """
                namespace App;

                public sealed class IncrementalMessage
                {
                    public string Value { get; } = "watch";
                }
                """),
            filePath: "/repo/App/IncrementalMessage.cs");

        var payload = await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            updatedSolution,
            workspace,
            ct: TestContext.Current.CancellationToken);

        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "App.IncrementalMessage");
        payload.CodeNodes.Should().Contain(codeNode => codeNode.RelativeFilePath == "App/IncrementalMessage.cs");
    }

    [Fact]
    public async Task WhenExtractingDocumentedMethod_ThenItBuildsSemanticSearchTextAndBodyHash()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        using var fixture = CreateFixtureSolution();
        const string documentedSource = """
            namespace App;

            public sealed class DocumentedProcessor
            {
                /// <summary>
                /// Processes inbound messages.
                /// </summary>
                /// <remarks>
                /// Writes audit entries and returns <see langword="null"/> when empty.
                /// </remarks>
                /// <param name="payload">Ignored parameter text.</param>
                /// <returns>Ignored return text.</returns>
                /// <exception cref="System.InvalidOperationException">Ignored exception text.</exception>
                public string Process(string payload)
                {
                    return payload.Trim();
                }
            }
            """;
        var documentedDocumentId = DocumentId.CreateNewId(fixture.AppProject.Id, "DocumentedProcessor.cs");
        var updatedSolution = fixture.Solution.AddDocument(
            documentedDocumentId,
            "DocumentedProcessor.cs",
            SourceText.From(documentedSource),
            filePath: "/repo/App/DocumentedProcessor.cs");

        var payload = await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            updatedSolution,
            workspace,
            ct: TestContext.Current.CancellationToken);
        var documentedMethod = payload.CodeNodes.Should()
            .ContainSingle(codeNode => codeNode.FullyQualifiedName == "App.DocumentedProcessor.Process(string)").Subject;
        var fallbackMethod = payload.CodeNodes.Should()
            .ContainSingle(codeNode => codeNode.FullyQualifiedName == "App.MessageConsumer.Render()").Subject;
        var interfaceMethod = payload.CodeNodes.Should()
            .ContainSingle(codeNode => codeNode.FullyQualifiedName == "Contracts.IMessageProvider.GetMessage()").Subject;

        documentedMethod.Summary.Should().Be("Processes inbound messages.\nWrites audit entries and returns null when empty.");
        documentedMethod.Summary.Should().NotContain("Ignored parameter text");
        documentedMethod.Summary.Should().NotContain("Ignored return text");
        documentedMethod.Summary.Should().NotContain("Ignored exception text");
        documentedMethod.SearchText.Should().Be("DocumentedProcessor.Process(string)\nProcesses inbound messages.\nWrites audit entries and returns null when empty.");
        documentedMethod.BodyHash.Should().NotBeNullOrWhiteSpace();
        fallbackMethod.SearchText.Should().Be("MessageConsumer.Render()");
        interfaceMethod.BodyHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task WhenExtractingSolution_ThenEmitsSharpSenseTraceActivities()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        using var fixture = CreateFixtureSolution();
        using var parent = new Activity("test-extraction").SetIdFormat(ActivityIdFormat.W3C).Start();
        var activityNames = new ConcurrentQueue<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == SharpSenseTraceSpan.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.TraceId == parent.TraceId)
                {
                    activityNames.Enqueue(activity.OperationName);
                }
            }
        };

        ActivitySource.AddActivityListener(listener);

        await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            fixture.Solution,
            workspace,
            ct: TestContext.Current.CancellationToken);

        activityNames.Should().Contain("roslyn.extract");
        activityNames.Should().Contain("roslyn.build-project-nodes");
        activityNames.Should().Contain("roslyn.build-code-nodes");
        activityNames.Should().Contain("roslyn.build-dependency-edges");
        activityNames.Should().Contain("roslyn.parse-project");
    }

    private static void AssertContainsEdge(
        IReadOnlyCollection<DependencyEdge> edges,
        IReadOnlyDictionary<string, string> fullyQualifiedNamesById,
        string callerFullyQualifiedName,
        string calleeFullyQualifiedName,
        EdgeType edgeType)
        => edges.Should().Contain(
            edge => edge.EdgeType == edgeType &&
                    fullyQualifiedNamesById.GetValueOrDefault(edge.CallerId) == callerFullyQualifiedName &&
                    fullyQualifiedNamesById.GetValueOrDefault(edge.CalleeId) == calleeFullyQualifiedName);

    private static MockFileSystem CreateRepositoryFileSystem()
    {
        return new MockFileSystem(
            new Dictionary<string, MockFileData>
            {
                ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
                ["/repo/CommandPipelineFixture.sln"] = new(string.Empty),
                ["/repo/App/App.csproj"] = new("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                ["/repo/Contracts/Contracts.csproj"] = new("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
            },
            "/repo");
    }

    private static IRepositoryWorkspace CreateRepositoryWorkspace(MockFileSystem fileSystem)
        => new RepositoryWorkspace("/repo", "/test-storage/index.db", fileSystem);

    private static ServiceProvider CreateServiceProvider(IFileSystem fileSystem)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fileSystem);
        services.AddIndexingInfrastructure();

        return services.BuildServiceProvider();
    }

    private static FixtureSolution CreateFixtureSolution()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var metadataReferences = GetMetadataReferences();
        var contractsProjectId = ProjectId.CreateNewId("Contracts");
        var appProjectId = ProjectId.CreateNewId("App");

        solution = solution.AddProject(ProjectInfo.Create(
            contractsProjectId,
            VersionStamp.Create(),
            "Contracts",
            "Contracts",
            LanguageNames.CSharp,
            filePath: "/repo/Contracts/Contracts.csproj",
            metadataReferences: metadataReferences));
        solution = solution.AddProject(ProjectInfo.Create(
            appProjectId,
            VersionStamp.Create(),
            "App",
            "App",
            LanguageNames.CSharp,
            filePath: "/repo/App/App.csproj",
            metadataReferences: metadataReferences));
        solution = solution.AddProjectReference(appProjectId, new ProjectReference(contractsProjectId));

        var contractsDocumentId = DocumentId.CreateNewId(contractsProjectId, "IMessageProvider.cs");
        solution = solution.AddDocument(
            contractsDocumentId,
            "IMessageProvider.cs",
            SourceText.From(
                """
                namespace Contracts;

                public interface IMessageProvider
                {
                    string Name { get; }

                    string GetMessage();
                }
                """),
            filePath: "/repo/Contracts/IMessageProvider.cs");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(contractsProjectId, "IPaymentGateway.cs"),
            "IPaymentGateway.cs",
            SourceText.From(
                """
                namespace Contracts;
                
                public interface IPaymentGateway
                {
                   void ProcessPayment();
                }
                """),
            filePath: "/repo/Contracts/IPaymentGateway.cs");

        var messageProviderDocumentId = DocumentId.CreateNewId(appProjectId, "MessageProvider.cs");
        solution = solution.AddDocument(
            messageProviderDocumentId,
            "MessageProvider.cs",
            SourceText.From(
                """
                using Contracts;

                namespace App;

                public sealed class MessageProvider : IMessageProvider
                {
                    private readonly string _prefix = "Hello";

                    public string Name => "provider";

                    public string GetMessage()
                    {
                        return _prefix;
                    }
                }
                """),
            filePath: "/repo/App/MessageProvider.cs");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(appProjectId, "Mock.cs"),
            "Mock.cs",
            SourceText.From(
                """
                using System;
                using System.Linq.Expressions;
                
                namespace App;
                
                public sealed class Mock<T>
                {
                   public void Setup(Expression<Action<T>> expression)
                   {
                   }
                }
                """),
            filePath: "/repo/App/Mock.cs");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(appProjectId, "OuterProcessor.cs"),
            "OuterProcessor.cs",
            SourceText.From(
                """
                namespace App;
                
                public sealed class OuterProcessor
                {
                   public sealed class InnerProcessor
                   {
                       public void Execute()
                       {
                       }
                   }
                }
                """),
            filePath: "/repo/App/OuterProcessor.cs");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(appProjectId, "PartialProcessor.Part1.cs"),
            "PartialProcessor.Part1.cs",
            SourceText.From(
                """
                namespace App;
                
                public sealed partial class PartialProcessor
                {
                   public string Build()
                   {
                       return "build";
                   }
                }
                """),
            filePath: "/repo/App/PartialProcessor.Part1.cs");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(appProjectId, "PartialProcessor.Part2.cs"),
            "PartialProcessor.Part2.cs",
            SourceText.From(
                """
                namespace App;
                
                public sealed partial class PartialProcessor
                {
                   public string Format()
                   {
                       return "format";
                   }
                }
                """),
            filePath: "/repo/App/PartialProcessor.Part2.cs");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(appProjectId, "MessageConsumer.cs"),
            "MessageConsumer.cs",
            SourceText.From(
                """
                using Contracts;

                namespace App;

                public sealed class MessageConsumer(IMessageProvider messageProvider)
                {
                    public string Render()
                    {
                        return messageProvider.GetMessage();
                    }
                }
                """),
            filePath: "/repo/App/MessageConsumer.cs");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(appProjectId, "PaymentGatewayExpectationBuilder.cs"),
            "PaymentGatewayExpectationBuilder.cs",
            SourceText.From(
                """
                using Contracts;
                
                namespace App;
                
                public sealed class PaymentGatewayExpectationBuilder
                {
                   public void Configure()
                   {
                       var mock = new Mock<IPaymentGateway>();
                       mock.Setup(x => x.ProcessPayment());
                   }
                }
                """),
            filePath: "/repo/App/PaymentGatewayExpectationBuilder.cs");
        solution = solution.AddDocument(
            DocumentId.CreateNewId(appProjectId, "ServiceRegistrationExtensions.cs"),
            "ServiceRegistrationExtensions.cs",
            SourceText.From(
                """
                using Contracts;
                using Microsoft.Extensions.DependencyInjection;

                namespace App;

                public static class ServiceRegistrationExtensions
                {
                    public static IServiceCollection AddMessagePipeline(this IServiceCollection services)
                    {
                        services.AddSingleton<IMessageProvider, MessageProvider>();
                        services.AddTransient<MessageConsumer>();
                        return services;
                    }
                }
                """),
            filePath: "/repo/App/ServiceRegistrationExtensions.cs");

        return new FixtureSolution(
            workspace,
            solution,
            solution.GetProject(appProjectId) ?? throw new InvalidOperationException("App project was not created."),
            messageProviderDocumentId);
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var trustedPlatformAssemblies = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? [];
        var locations = new HashSet<string>(trustedPlatformAssemblies, StringComparer.OrdinalIgnoreCase)
        {
            typeof(IServiceCollection).Assembly.Location
        };

        return [.. locations.Select(static location => MetadataReference.CreateFromFile(location))];
    }

    private sealed record FixtureSolution(
        AdhocWorkspace Workspace,
        Solution Solution,
        Project AppProject,
        DocumentId MessageProviderDocumentId) : IDisposable
    {
        public void Dispose() => Workspace.Dispose();
    }
}
