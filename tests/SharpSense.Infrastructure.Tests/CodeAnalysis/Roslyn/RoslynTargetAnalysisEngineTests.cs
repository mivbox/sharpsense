using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpSense.Application.Indexing.Models;
using SharpSense.Application.Shared.Diagnostics;
using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.CodeAnalysis.Roslyn;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Storage;

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
        var fixture = CreateFixtureSolution();

        var payload = await engine.Extract(
            "/repo/CommandPipelineFixture.sln",
            fixture.Solution,
            workspace,
            ct: TestContext.Current.CancellationToken);
        var fullyQualifiedNamesById = payload.CodeNodes.ToDictionary(
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
    public async Task WhenExtractingProject_ThenReturnsProjectBackedPayload()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        var fixture = CreateFixtureSolution();

        var payload = await engine.Extract(
            "/repo/App/App.csproj",
            fixture.AppProject,
            workspace,
            ct: TestContext.Current.CancellationToken);

        payload.TargetPath.Should().Be("/repo/App/App.csproj");
        payload.Projects.Should().NotBeEmpty();
        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "App.MessageProvider");
    }

    [Fact]
    public async Task WhenExtractingIncrementalModifiedDocument_ThenReturnsDeltaNodesAndEdges()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        var fixture = CreateFixtureSolution();
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

        var payload = await engine.ExtractIncremental(
            "/repo/CommandPipelineFixture.sln",
            updatedSolution,
            workspace,
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Modified,
                    NewPath: "/repo/App/MessageProvider.cs")
            ],
            ct: TestContext.Current.CancellationToken);
        var fullyQualifiedNamesById = payload.CodeNodes.ToDictionary(
            static codeNode => codeNode.CanonicalId,
            static codeNode => codeNode.FullyQualifiedName,
            StringComparer.Ordinal);

        payload.Projects.Should().BeEmpty();
        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "App.MessageProvider.GetCopiedMessage()");
        payload.CodeNodes.Should().OnlyContain(codeNode => codeNode.RelativeFilePath == "App/MessageProvider.cs");
        AssertContainsEdge(
            payload.Edges,
            fullyQualifiedNamesById,
            "App.MessageProvider.GetCopiedMessage()",
            "App.MessageProvider.GetMessage()",
            EdgeType.MethodCall);
    }

    [Fact]
    public async Task WhenExtractingIncrementalAddedDocument_ThenReturnsNewNodes()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        var fixture = CreateFixtureSolution();
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

        var payload = await engine.ExtractIncremental(
            "/repo/CommandPipelineFixture.sln",
            updatedSolution,
            workspace,
            [
                new WorkspaceFileChange(
                    WorkspaceFileChangeAction.Added,
                    NewPath: "/repo/App/IncrementalMessage.cs")
            ],
            ct: TestContext.Current.CancellationToken);

        payload.CodeNodes.Should().Contain(codeNode => codeNode.FullyQualifiedName == "App.IncrementalMessage");
        payload.CodeNodes.Should().OnlyContain(codeNode => codeNode.RelativeFilePath == "App/IncrementalMessage.cs");
    }

    [Fact]
    public async Task WhenExtractingDocumentedMethod_ThenItBuildsSemanticSearchTextAndBodyHash()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        var fixture = CreateFixtureSolution();
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
            .ContainSingle(codeNode => codeNode.FullyQualifiedName == "App.DocumentedProcessor.Process(string)")
            .Subject;
        var fallbackMethod = payload.CodeNodes.Should()
            .ContainSingle(codeNode => codeNode.FullyQualifiedName == "App.MessageConsumer.Render()")
            .Subject;
        var interfaceMethod = payload.CodeNodes.Should()
            .ContainSingle(codeNode => codeNode.FullyQualifiedName == "Contracts.IMessageProvider.GetMessage()")
            .Subject;

        documentedMethod.Summary.Should().Be("Processes inbound messages.\nWrites audit entries and returns null when empty.");
        documentedMethod.Summary.Should().NotContain("Ignored parameter text");
        documentedMethod.Summary.Should().NotContain("Ignored return text");
        documentedMethod.Summary.Should().NotContain("Ignored exception text");
        documentedMethod.SearchText.Should().Be("DocumentedProcessor.Process(string)\nProcesses inbound messages.\nWrites audit entries and returns null when empty.");
        documentedMethod.BodyHash.Should().Be(ComputeHash("\n        return payload.Trim();\n    "));
        fallbackMethod.SearchText.Should().Be("MessageConsumer.Render()");
        interfaceMethod.BodyHash.Should().BeNull();
    }

    [Fact]
    public async Task WhenExtractingSolution_ThenEmitsSharpSenseTraceActivities()
    {
        var fileSystem = CreateRepositoryFileSystem();
        var workspace = CreateRepositoryWorkspace(fileSystem);
        using var serviceProvider = CreateServiceProvider(fileSystem);
        var engine = serviceProvider.GetRequiredService<ITargetAnalysisEngine>();
        var fixture = CreateFixtureSolution();
        var activityNames = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == SharpSenseTraceSpan.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activityNames.Add(activity.OperationName)
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
        return new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main"),
            ["/repo/CommandPipelineFixture.sln"] = new(string.Empty),
            ["/repo/App/App.csproj"] = new("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
            ["/repo/Contracts/Contracts.csproj"] = new("<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
        }, "/repo");
    }

    private static IRepositoryWorkspace CreateRepositoryWorkspace(MockFileSystem fileSystem)
        => new RepositoryWorkspaceFactory(fileSystem).CreateFromWorkingDirectory("/repo");

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
                    public string Name => "provider";

                    public string GetMessage()
                    {
                        return "Hello";
                    }
                }
                """),
            filePath: "/repo/App/MessageProvider.cs");
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

    private static string ComputeHash(string bodyText)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(bodyText));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private sealed record FixtureSolution(
        Solution Solution,
        Project AppProject,
        DocumentId MessageProviderDocumentId);
}
