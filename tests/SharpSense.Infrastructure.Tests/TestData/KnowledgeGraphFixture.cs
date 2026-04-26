using SharpSense.Domain.KnowledgeGraph.Edges;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Domain.KnowledgeGraph.Nodes;
using SharpSense.Infrastructure.Persistence;

namespace SharpSense.Infrastructure.Tests.TestData;

internal static class KnowledgeGraphFixture
{
    public const string AppProjectId = "project:Fixture.App/Fixture.App.csproj";
    public const string CoreProjectId = "project:Fixture.Core/Fixture.Core.csproj";
    public const int TargetNodeId = 1;
    public const int DirectCallerNodeId = 2;
    public const int TransitiveCallerNodeId = 3;
    public const int ServiceRegistrationCallerNodeId = 4;
    public const int FormatterNodeId = 5;
    public const int MessageNodeId = 6;
    public const string TargetCanonicalId = "node-target";
    public const string DirectCallerCanonicalId = "node-direct-caller";
    public const string TransitiveCallerCanonicalId = "node-transitive-caller";
    public const string ServiceRegistrationCallerCanonicalId = "node-service-registration";
    public const string FormatterCanonicalId = "node-formatter";
    public const string MessageCanonicalId = "node-message";

    public const string TargetFullyQualifiedName = "Fixture.App.MessageProvider.GetMessage()";
    public const string DirectCallerFullyQualifiedName = "Fixture.App.MessageConsumer.Render()";
    public const string TransitiveCallerFullyQualifiedName = "Fixture.App.HttpEndpoint.Handle()";
    public const string ServiceRegistrationCallerFullyQualifiedName = "Fixture.App.ServiceRegistration.Configure(IServiceCollection)";
    public const string FormatterFullyQualifiedName = "Fixture.App.MessageFormatter.Format(string)";
    public const string MessageFullyQualifiedName = "Fixture.Core.Message";
    public const string TargetDisplayName = "MessageProvider.GetMessage()";
    public const string DirectCallerDisplayName = "MessageConsumer.Render()";
    public const string TransitiveCallerDisplayName = "HttpEndpoint.Handle()";
    public const string ServiceRegistrationCallerDisplayName = "ServiceRegistration.Configure(IServiceCollection)";
    public const string FormatterDisplayName = "MessageFormatter.Format(string)";
    public const string MessageDisplayName = "Message";

    public static async Task SeedAsync(SharpSenseDbContext context)
    {
        context.CodeNodes.AddRange(
            new CodeNode
            {
                Id = TargetNodeId,
                CanonicalId = TargetCanonicalId,
                ProjectId = AppProjectId,
                FullyQualifiedName = TargetFullyQualifiedName,
                DisplayName = TargetDisplayName,
                NodeType = NodeType.Method,
                RelativeFilePath = "src/Fixture.App/MessageProvider.cs",
                StartLine = 10,
                EndLine = 14,
                Summary = "Gets a message.",
                VectorEmbedding = [1f, 0f]
            },
            new CodeNode
            {
                Id = DirectCallerNodeId,
                CanonicalId = DirectCallerCanonicalId,
                ProjectId = AppProjectId,
                FullyQualifiedName = DirectCallerFullyQualifiedName,
                DisplayName = DirectCallerDisplayName,
                NodeType = NodeType.Method,
                RelativeFilePath = "src/Fixture.App/MessageConsumer.cs",
                StartLine = 20,
                EndLine = 28,
                Summary = "Renders a message.",
                VectorEmbedding = [0.7f, 0.3f]
            },
            new CodeNode
            {
                Id = TransitiveCallerNodeId,
                CanonicalId = TransitiveCallerCanonicalId,
                ProjectId = AppProjectId,
                FullyQualifiedName = TransitiveCallerFullyQualifiedName,
                DisplayName = TransitiveCallerDisplayName,
                NodeType = NodeType.Method,
                RelativeFilePath = "src/Fixture.App/HttpEndpoint.cs",
                StartLine = 5,
                EndLine = 12,
                Summary = "Handles HTTP requests.",
                VectorEmbedding = [0.6f, 0.4f]
            },
            new CodeNode
            {
                Id = ServiceRegistrationCallerNodeId,
                CanonicalId = ServiceRegistrationCallerCanonicalId,
                ProjectId = AppProjectId,
                FullyQualifiedName = ServiceRegistrationCallerFullyQualifiedName,
                DisplayName = ServiceRegistrationCallerDisplayName,
                NodeType = NodeType.Method,
                RelativeFilePath = "src/Fixture.App/ServiceRegistration.cs",
                StartLine = 4,
                EndLine = 14,
                Summary = "Registers the message pipeline.",
                VectorEmbedding = [0.2f, 0.8f]
            },
            new CodeNode
            {
                Id = FormatterNodeId,
                CanonicalId = FormatterCanonicalId,
                ProjectId = AppProjectId,
                FullyQualifiedName = FormatterFullyQualifiedName,
                DisplayName = FormatterDisplayName,
                NodeType = NodeType.Method,
                RelativeFilePath = "src/Fixture.App/MessageFormatter.cs",
                StartLine = 7,
                EndLine = 11,
                Summary = "Formats messages.",
                VectorEmbedding = [0.95f, 0.05f]
            },
            new CodeNode
            {
                Id = MessageNodeId,
                CanonicalId = MessageCanonicalId,
                ProjectId = CoreProjectId,
                FullyQualifiedName = MessageFullyQualifiedName,
                DisplayName = MessageDisplayName,
                NodeType = NodeType.Class,
                RelativeFilePath = "src/Fixture.Core/Message.cs",
                StartLine = 1,
                EndLine = 12,
                Summary = "Message model.",
                VectorEmbedding = [0.4f, 0.6f]
            });

        context.DependencyEdges.AddRange(
            new DependencyEdge
            {
                CallerId = DirectCallerCanonicalId,
                CalleeId = TargetCanonicalId,
                EdgeType = EdgeType.MethodCall
            },
            new DependencyEdge
            {
                CallerId = TransitiveCallerCanonicalId,
                CalleeId = DirectCallerCanonicalId,
                EdgeType = EdgeType.MethodCall
            },
            new DependencyEdge
            {
                CallerId = ServiceRegistrationCallerCanonicalId,
                CalleeId = TargetCanonicalId,
                EdgeType = EdgeType.ServiceRegistration
            },
            new DependencyEdge
            {
                CallerId = TargetCanonicalId,
                CalleeId = FormatterCanonicalId,
                EdgeType = EdgeType.MethodCall
            },
            new DependencyEdge
            {
                CallerId = TargetCanonicalId,
                CalleeId = FormatterCanonicalId,
                EdgeType = EdgeType.Instantiates
            },
            new DependencyEdge
            {
                CallerId = TargetCanonicalId,
                CalleeId = MessageCanonicalId,
                EdgeType = EdgeType.Instantiates
            });

        await context.SaveChangesAsync();
    }
}
