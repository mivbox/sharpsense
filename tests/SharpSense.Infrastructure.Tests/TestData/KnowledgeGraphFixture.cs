using Microsoft.EntityFrameworkCore;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Persistence;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Tests.TestData;

internal static class KnowledgeGraphFixture
{
    public const string AppProjectId = "project:Fixture.App/Fixture.App.csproj";
    public const string CoreProjectId = "project:Fixture.Core/Fixture.Core.csproj";
    private const int RootDirectoryId = 1;
    private const int SrcDirectoryId = 2;
    private const int AppDirectoryId = 3;
    private const int CoreDirectoryId = 4;
    private const int AppProjectNodeId = 100;
    private const int CoreProjectNodeId = 101;
    public const int TargetNodeId = 1;
    public const int DirectCallerNodeId = 2;
    public const int TransitiveCallerNodeId = 3;
    public const int ServiceRegistrationCallerNodeId = 4;
    public const int FormatterNodeId = 5;
    public const int MessageNodeId = 6;
    public const int MessageProviderTypeNodeId = 7;
    public const int CachedMessageNodeId = 8;
    public const string TargetCanonicalId = "node-target";
    public const string DirectCallerCanonicalId = "node-direct-caller";
    public const string TransitiveCallerCanonicalId = "node-transitive-caller";
    public const string ServiceRegistrationCallerCanonicalId = "node-service-registration";
    public const string FormatterCanonicalId = "node-formatter";
    public const string MessageCanonicalId = "node-message";
    public const string MessageProviderTypeCanonicalId = "node-message-provider-type";
    public const string CachedMessageCanonicalId = "node-cached-message";

    public const string TargetFullyQualifiedName = "Fixture.App.MessageProvider.GetMessage()";
    public const string DirectCallerFullyQualifiedName = "Fixture.App.MessageConsumer.Render()";
    public const string TransitiveCallerFullyQualifiedName = "Fixture.App.HttpEndpoint.Handle()";
    public const string ServiceRegistrationCallerFullyQualifiedName = "Fixture.App.ServiceRegistration.Configure(IServiceCollection)";
    public const string FormatterFullyQualifiedName = "Fixture.App.MessageFormatter.Format(string)";
    public const string MessageFullyQualifiedName = "Fixture.Core.Message";
    public const string MessageProviderTypeFullyQualifiedName = "Fixture.App.MessageProvider";
    public const string CachedMessageFullyQualifiedName = "Fixture.App.MessageProvider.GetCachedMessage()";
    public const string TargetDisplayName = "MessageProvider.GetMessage()";
    public const string DirectCallerDisplayName = "MessageConsumer.Render()";
    public const string TransitiveCallerDisplayName = "HttpEndpoint.Handle()";
    public const string ServiceRegistrationCallerDisplayName = "ServiceRegistration.Configure(IServiceCollection)";
    public const string FormatterDisplayName = "MessageFormatter.Format(string)";
    public const string MessageDisplayName = "Message";
    public const string MessageProviderTypeDisplayName = "MessageProvider";
    public const string CachedMessageDisplayName = "MessageProvider.GetCachedMessage()";

    public static async Task SeedAsync(SharpSenseDbContext context)
    {
        context.Directories.AddRange(
            new DirectoryRecord
            {
                Id = RootDirectoryId,
                Path = string.Empty,
                Name = "/"
            },
            new DirectoryRecord
            {
                Id = SrcDirectoryId,
                ParentId = RootDirectoryId,
                Path = "src",
                Name = "src"
            },
            new DirectoryRecord
            {
                Id = AppDirectoryId,
                ParentId = SrcDirectoryId,
                Path = "src/Fixture.App",
                Name = "Fixture.App"
            },
            new DirectoryRecord
            {
                Id = CoreDirectoryId,
                ParentId = SrcDirectoryId,
                Path = "src/Fixture.Core",
                Name = "Fixture.Core"
            });
        context.DirectoryClosures.AddRange(
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = RootDirectoryId,
                DescendantDirectoryId = RootDirectoryId,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = SrcDirectoryId,
                DescendantDirectoryId = SrcDirectoryId,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = AppDirectoryId,
                DescendantDirectoryId = AppDirectoryId,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = CoreDirectoryId,
                DescendantDirectoryId = CoreDirectoryId,
                Depth = 0
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = RootDirectoryId,
                DescendantDirectoryId = SrcDirectoryId,
                Depth = 1
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = RootDirectoryId,
                DescendantDirectoryId = AppDirectoryId,
                Depth = 2
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = RootDirectoryId,
                DescendantDirectoryId = CoreDirectoryId,
                Depth = 2
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = SrcDirectoryId,
                DescendantDirectoryId = AppDirectoryId,
                Depth = 1
            },
            new DirectoryClosureRecord
            {
                AncestorDirectoryId = SrcDirectoryId,
                DescendantDirectoryId = CoreDirectoryId,
                Depth = 1
            });
        context.Documents.AddRange(
            new DocumentRecord
            {
                Id = 10,
                DirectoryId = AppDirectoryId,
                FileName = "Fixture.App.csproj",
                Extension = ".csproj",
                RelativePath = "src/Fixture.App/Fixture.App.csproj",
                Kind = DocumentKind.ProjectFile
            },
            new DocumentRecord
            {
                Id = 11,
                DirectoryId = CoreDirectoryId,
                FileName = "Fixture.Core.csproj",
                Extension = ".csproj",
                RelativePath = "src/Fixture.Core/Fixture.Core.csproj",
                Kind = DocumentKind.ProjectFile
            },
            new DocumentRecord
            {
                Id = 12,
                DirectoryId = AppDirectoryId,
                FileName = "MessageProvider.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/MessageProvider.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = 13,
                DirectoryId = AppDirectoryId,
                FileName = "MessageConsumer.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/MessageConsumer.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = 14,
                DirectoryId = AppDirectoryId,
                FileName = "HttpEndpoint.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/HttpEndpoint.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = 15,
                DirectoryId = AppDirectoryId,
                FileName = "ServiceRegistration.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/ServiceRegistration.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = 16,
                DirectoryId = AppDirectoryId,
                FileName = "MessageFormatter.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.App/MessageFormatter.cs",
                Kind = DocumentKind.Source
            },
            new DocumentRecord
            {
                Id = 17,
                DirectoryId = CoreDirectoryId,
                FileName = "Message.cs",
                Extension = ".cs",
                RelativePath = "src/Fixture.Core/Message.cs",
                Kind = DocumentKind.Source
            });
        context.GraphNodes.AddRange(
            new GraphNodeRecord
            {
                Id = AppProjectNodeId,
                CanonicalId = AppProjectId,
                Kind = GraphNodeKind.Project
            },
            new GraphNodeRecord
            {
                Id = CoreProjectNodeId,
                CanonicalId = CoreProjectId,
                Kind = GraphNodeKind.Project
            },
            new GraphNodeRecord
            {
                Id = TargetNodeId,
                CanonicalId = TargetCanonicalId,
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = DirectCallerNodeId,
                CanonicalId = DirectCallerCanonicalId,
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = TransitiveCallerNodeId,
                CanonicalId = TransitiveCallerCanonicalId,
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = ServiceRegistrationCallerNodeId,
                CanonicalId = ServiceRegistrationCallerCanonicalId,
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = FormatterNodeId,
                CanonicalId = FormatterCanonicalId,
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = MessageNodeId,
                CanonicalId = MessageCanonicalId,
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = MessageProviderTypeNodeId,
                CanonicalId = MessageProviderTypeCanonicalId,
                Kind = GraphNodeKind.Code
            },
            new GraphNodeRecord
            {
                Id = CachedMessageNodeId,
                CanonicalId = CachedMessageCanonicalId,
                Kind = GraphNodeKind.Code
            });
        context.ProjectNodes.AddRange(
            new ProjectNodeRecord
            {
                Id = AppProjectNodeId,
                Name = "Fixture.App",
                ProjectDocumentId = 10,
                ContentHash = "fixture-app"
            },
            new ProjectNodeRecord
            {
                Id = CoreProjectNodeId,
                Name = "Fixture.Core",
                ProjectDocumentId = 11,
                ContentHash = "fixture-core"
            });
        context.CodeNodes.AddRange(
            new CodeNodeRecord
            {
                Id = TargetNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 12,
                FullyQualifiedName = TargetFullyQualifiedName,
                DisplayName = TargetDisplayName,
                NodeType = NodeType.Method,
                StartLine = 10,
                EndLine = 14,
                Summary = "Gets a message.",
                SearchText = "MessageProvider.GetMessage()\nGets a message.",
                VectorEmbedding = [1f, 0f]
            },
            new CodeNodeRecord
            {
                Id = DirectCallerNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 13,
                FullyQualifiedName = DirectCallerFullyQualifiedName,
                DisplayName = DirectCallerDisplayName,
                NodeType = NodeType.Method,
                StartLine = 20,
                EndLine = 28,
                Summary = "Renders a message.",
                SearchText = "MessageConsumer.Render()\nRenders a message.",
                VectorEmbedding = [0.7f, 0.3f]
            },
            new CodeNodeRecord
            {
                Id = TransitiveCallerNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 14,
                FullyQualifiedName = TransitiveCallerFullyQualifiedName,
                DisplayName = TransitiveCallerDisplayName,
                NodeType = NodeType.Method,
                StartLine = 5,
                EndLine = 12,
                Summary = "Handles HTTP requests.",
                SearchText = "HttpEndpoint.Handle()\nHandles HTTP requests.",
                VectorEmbedding = [0.6f, 0.4f]
            },
            new CodeNodeRecord
            {
                Id = ServiceRegistrationCallerNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 15,
                FullyQualifiedName = ServiceRegistrationCallerFullyQualifiedName,
                DisplayName = ServiceRegistrationCallerDisplayName,
                NodeType = NodeType.Method,
                StartLine = 4,
                EndLine = 14,
                Summary = "Registers the message pipeline.",
                SearchText = "ServiceRegistration.Configure(IServiceCollection)\nRegisters the message pipeline.",
                VectorEmbedding = [0.2f, 0.8f]
            },
            new CodeNodeRecord
            {
                Id = FormatterNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 16,
                FullyQualifiedName = FormatterFullyQualifiedName,
                DisplayName = FormatterDisplayName,
                NodeType = NodeType.Method,
                StartLine = 7,
                EndLine = 11,
                Summary = "Formats messages.",
                SearchText = "MessageFormatter.Format(string)\nFormats messages.",
                VectorEmbedding = [0.95f, 0.05f]
            },
            new CodeNodeRecord
            {
                Id = MessageNodeId,
                ProjectNodeId = CoreProjectNodeId,
                DocumentId = 17,
                FullyQualifiedName = MessageFullyQualifiedName,
                DisplayName = MessageDisplayName,
                NodeType = NodeType.Class,
                StartLine = 1,
                EndLine = 12,
                Summary = "Message model.",
                SearchText = "Message\nMessage model.",
                VectorEmbedding = [0.4f, 0.6f]
            },
            new CodeNodeRecord
            {
                Id = MessageProviderTypeNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 12,
                FullyQualifiedName = MessageProviderTypeFullyQualifiedName,
                DisplayName = MessageProviderTypeDisplayName,
                NodeType = NodeType.Class,
                StartLine = 1,
                EndLine = 24,
                Summary = "Provides messages.",
                SearchText = "MessageProvider\nProvides messages.",
                VectorEmbedding = [0.85f, 0.15f]
            },
            new CodeNodeRecord
            {
                Id = CachedMessageNodeId,
                ProjectNodeId = AppProjectNodeId,
                DocumentId = 12,
                FullyQualifiedName = CachedMessageFullyQualifiedName,
                DisplayName = CachedMessageDisplayName,
                NodeType = NodeType.Method,
                StartLine = 16,
                EndLine = 20,
                Summary = "Returns cached messages.",
                SearchText = "MessageProvider.GetCachedMessage()\nReturns cached messages.",
                VectorEmbedding = [0.5f, 0.5f]
            });

        context.DependencyEdges.AddRange(
            new DependencyEdgeRecord
            {
                CallerNodeId = AppProjectNodeId,
                CalleeNodeId = MessageProviderTypeNodeId,
                EdgeType = EdgeType.ParentOf
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = MessageProviderTypeNodeId,
                CalleeNodeId = TargetNodeId,
                EdgeType = EdgeType.ParentOf
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = MessageProviderTypeNodeId,
                CalleeNodeId = CachedMessageNodeId,
                EdgeType = EdgeType.ParentOf
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = DirectCallerNodeId,
                CalleeNodeId = TargetNodeId,
                EdgeType = EdgeType.MethodCall
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = TransitiveCallerNodeId,
                CalleeNodeId = DirectCallerNodeId,
                EdgeType = EdgeType.MethodCall
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = ServiceRegistrationCallerNodeId,
                CalleeNodeId = TargetNodeId,
                EdgeType = EdgeType.ServiceRegistration
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = TargetNodeId,
                CalleeNodeId = FormatterNodeId,
                EdgeType = EdgeType.MethodCall
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = TargetNodeId,
                CalleeNodeId = FormatterNodeId,
                EdgeType = EdgeType.Instantiates
            },
            new DependencyEdgeRecord
            {
                CallerNodeId = TargetNodeId,
                CalleeNodeId = MessageNodeId,
                EdgeType = EdgeType.Instantiates
            });

        await context.SaveChangesAsync();

        if (await FtsTableExistsAsync(context))
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO CodeNodeSearch (Id, CanonicalId, DisplayName, FullyQualifiedName, SearchText, RelativeFilePath)
                SELECT CodeNodes.Id,
                       GraphNodes.CanonicalId,
                       CodeNodes.DisplayName,
                       CodeNodes.FullyQualifiedName,
                       CodeNodes.SearchText,
                       Documents.RelativePath
                FROM CodeNodes
                INNER JOIN GraphNodes ON GraphNodes.Id = CodeNodes.Id
                INNER JOIN Documents ON Documents.Id = CodeNodes.DocumentId;
                """);
        }
    }

    private static async Task<bool> FtsTableExistsAsync(SharpSenseDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='CodeNodeSearch');";
        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
    }
}
