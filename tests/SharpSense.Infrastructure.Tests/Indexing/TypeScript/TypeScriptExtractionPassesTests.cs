using AwesomeAssertions;
using SharpSense.Application.Indexing.Models;
using SharpSense.Domain.KnowledgeGraph.Enums;
using SharpSense.Infrastructure.Indexing;
using SharpSense.Infrastructure.Indexing.TypeScript;
using SharpSense.Infrastructure.Storage;
using System.IO.Abstractions.TestingHelpers;

namespace SharpSense.Infrastructure.Tests.Indexing.TypeScript;

public sealed class TypeScriptExtractionPassesTests
{
    [Fact]
    public async Task WhenExtractingExportedTypeScriptDeclarations_ThenReturnsFunctionsClassesAndComponents()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }"),
            ("/repo/src/Feature.tsx",
                """
                export function Toolbar() {
                    return <section />;
                }

                export function run() {
                    return 1;
                }

                export class Greeter {}

                export const App = () => <div />;

                const Hidden = () => <span />;
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.CodeNodes.Should().HaveCount(4);
        result.Value.CodeNodes.Select(static node => node.CanonicalId)
            .Should()
            .Equal(
                "code:ts:src/Feature.tsx:App",
                "code:ts:src/Feature.tsx:Greeter",
                "code:ts:src/Feature.tsx:Toolbar",
                "code:ts:src/Feature.tsx:run");
        result.Value.CodeNodes.Should().Contain(node =>
            node.CanonicalId == "code:ts:src/Feature.tsx:run" &&
            node.DisplayName == "run()" &&
            node.NodeType == NodeType.Method);
        result.Value.CodeNodes.Should().Contain(node =>
            node.CanonicalId == "code:ts:src/Feature.tsx:Greeter" &&
            node.DisplayName == "Greeter" &&
            node.NodeType == NodeType.Class);
        result.Value.CodeNodes.Should().Contain(node =>
            node.CanonicalId == "code:ts:src/Feature.tsx:App" &&
            node.DisplayName == "App" &&
            node.NodeType == NodeType.Component);
        result.Value.CodeNodes.Should().Contain(node =>
            node.CanonicalId == "code:ts:src/Feature.tsx:Toolbar" &&
            node.DisplayName == "Toolbar" &&
            node.NodeType == NodeType.Component);
    }

    [Fact]
    public async Task WhenExtractingAliasImports_ThenCreatesImportEdgesToResolvedExports()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json",
                """
                {
                  "compilerOptions": {
                    "baseUrl": ".",
                    "paths": {
                      "@shared/*": ["src/shared/*"]
                    }
                  }
                }
                """),
            ("/repo/src/shared/api.ts",
                """
                export function api() {
                    return 1;
                }
                """),
            ("/repo/src/App.ts",
                """
                import { api } from '@shared/api';

                export function run() {
                    return api();
                }
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.Edges.Should().ContainSingle(edge =>
            edge.CallerId == "code:ts:src/App.ts:run" &&
            edge.CalleeId == "code:ts:src/shared/api.ts:api" &&
            edge.EdgeType == EdgeType.Import);
    }

    [Fact]
    public async Task WhenExtractingExportedConstHooks_ThenReturnsMethodNodesAndImportEdges()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }"),
            ("/repo/src/state.tsx",
                """
                export const useAskPolicyDrawerProvider = () => {
                    return {
                        selectedHistory: null
                    };
                };
                """),
            ("/repo/src/App.tsx",
                """
                import { useAskPolicyDrawerProvider } from "./state";

                export function App() {
                    return useAskPolicyDrawerProvider();
                }
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.CodeNodes.Should().Contain(node =>
            node.CanonicalId == "code:ts:src/state.tsx:useAskPolicyDrawerProvider" &&
            node.NodeType == NodeType.Method);
        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/App.tsx:App" &&
            edge.CalleeId == "code:ts:src/state.tsx:useAskPolicyDrawerProvider" &&
            edge.EdgeType == EdgeType.Import);
    }

    [Fact]
    public async Task WhenPassesAreProvidedOutOfOrder_ThenExtractorStillProducesImportEdges()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json",
                """
                {
                  "compilerOptions": {
                    "baseUrl": ".",
                    "paths": {
                      "@shared/*": ["src/shared/*"]
                    }
                  }
                }
                """),
            ("/repo/src/shared/api.ts",
                """
                export function api() {
                    return 1;
                }
                """),
            ("/repo/src/App.ts",
                """
                import { api } from '@shared/api';

                export function run() {
                    return api();
                }
                """));
        var extractor = CreateExtractor(fileSystem, reversePassOrder: true);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.Edges.Should().ContainSingle(edge =>
            edge.CallerId == "code:ts:src/App.ts:run" &&
            edge.CalleeId == "code:ts:src/shared/api.ts:api" &&
            edge.EdgeType == EdgeType.Import);
    }

    [Fact]
    public async Task WhenExtractingHttpClientCalls_ThenCreatesHttpRequestEdgesWithUrlMetadata()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }"),
            ("/repo/src/Api.ts",
                """
                export function loadUsers() {
                    return fetch("/api/users");
                }

                export function saveUser() {
                    return axios.post("/api/users", {});
                }
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/Api.ts:loadUsers" &&
            edge.CalleeId == "http:GET:%2Fapi%2Fusers" &&
            edge.EdgeType == EdgeType.HttpRequest &&
            edge.Metadata == "/api/users");
        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/Api.ts:saveUser" &&
            edge.CalleeId == "http:POST:%2Fapi%2Fusers" &&
            edge.EdgeType == EdgeType.HttpRequest &&
            edge.Metadata == "/api/users");
    }

    [Fact]
    public async Task WhenExtractingExternalImports_ThenItKeepsThemAsPackagesEvenWhenUsedInJsx()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }"),
            ("/repo/src/WorkspaceTree.tsx",
                """
                import { Alert, Box } from "@mui/material";
                import { useEffect } from "react";

                export function WorkspaceTree() {
                    useEffect(() => {}, []);

                    return (
                        <Box>
                            <Alert severity="info">Ready</Alert>
                        </Box>
                    );
                }
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/WorkspaceTree.tsx:WorkspaceTree" &&
            edge.CalleeId == $"package:{Uri.EscapeDataString("@mui/material")}:{Uri.EscapeDataString("Alert")}" &&
            edge.EdgeType == EdgeType.Import &&
            edge.Metadata == null);
        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/WorkspaceTree.tsx:WorkspaceTree" &&
            edge.CalleeId == $"package:{Uri.EscapeDataString("@mui/material")}:{Uri.EscapeDataString("Box")}" &&
            edge.EdgeType == EdgeType.Import &&
            edge.Metadata == null);
        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/WorkspaceTree.tsx:WorkspaceTree" &&
            edge.CalleeId == $"package:{Uri.EscapeDataString("react")}:{Uri.EscapeDataString("useEffect")}" &&
            edge.EdgeType == EdgeType.Import &&
            edge.Metadata == null);
    }

    [Fact]
    public async Task WhenExtractingDefaultExternalImportsUsedInJsx_ThenItKeepsThemAsPackages()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }"),
            ("/repo/src/Graph.tsx",
                """
                import ForceGraph3D from "react-force-graph-3d";

                export function Graph() {
                    return <ForceGraph3D />;
                }
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/Graph.tsx:Graph" &&
            edge.CalleeId == $"package:{Uri.EscapeDataString("react-force-graph-3d")}:{Uri.EscapeDataString("ForceGraph3D")}" &&
            edge.EdgeType == EdgeType.Import &&
            edge.Metadata == null);
    }

    [Fact]
    public async Task WhenExtractingNamespaceExternalImportsUsedInJsx_ThenItUsesTheMemberNameAsThePackageIdentity()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }"),
            ("/repo/src/Tree.tsx",
                """
                import * as Mui from "@mui/material";

                export function Tree() {
                    return <Mui.Box />;
                }
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/Tree.tsx:Tree" &&
            edge.CalleeId == $"package:{Uri.EscapeDataString("@mui/material")}:{Uri.EscapeDataString("Box")}" &&
            edge.EdgeType == EdgeType.Import &&
            edge.Metadata == null);
    }

    [Fact]
    public async Task WhenExtractingBareNamespaceJsxTag_ThenItFallsBackToThePackageImport()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }"),
            ("/repo/src/Tree.tsx",
                """
                import * as Mui from "@mui/material";

                export function Tree() {
                    return <Mui />;
                }
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/SharpSense.sln", null),
            TestContext.Current.CancellationToken);

        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:src/Tree.tsx:Tree" &&
            edge.CalleeId == $"package:{Uri.EscapeDataString("@mui/material")}" &&
            edge.EdgeType == EdgeType.Import &&
            edge.Metadata == null);
    }

    [Fact]
    public async Task WhenExtractingWorkspacePackageImports_ThenItIndexesReachableAliasTargetsAsInternalNodes()
    {
        var fileSystem = CreateRepositoryFileSystem(
            ("/repo/SharpSense.sln", string.Empty),
            ("/repo/apps/web/tsconfig.json",
                """
                {
                  "compilerOptions": {
                    "baseUrl": "."
                  }
                }
                """),
            ("/repo/apps/web/src/App.tsx",
                """
                import { useGetConversationById } from "@loanmarket/ai-chat-api/query-hooks/use-get-conversation-by-id-query.g";

                export function App() {
                    return useGetConversationById({
                        params: {
                            path: {
                                id: "123"
                            }
                        }
                    });
                }
                """),
            ("/repo/packages/ai-chat-api/package.json",
                """
                {
                  "name": "@loanmarket/ai-chat-api"
                }
                """),
            ("/repo/packages/ai-chat-api/tsconfig.json",
                """
                {
                  "compilerOptions": {
                    "baseUrl": "src"
                  }
                }
                """),
            ("/repo/packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts",
                """
                const useGetConversationById = () => {
                    return null;
                };

                export { useGetConversationById };
                """));
        var extractor = CreateExtractor(fileSystem);

        var result = await extractor.Extract(
            new ExtractionContext("/repo/apps/web/tsconfig.json", null),
            TestContext.Current.CancellationToken);

        result.Value.CodeNodes.Should().Contain(node =>
            node.CanonicalId ==
            "code:ts:packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts:useGetConversationById" &&
            node.NodeType == NodeType.Method);
        result.Value.Edges.Should().Contain(edge =>
            edge.CallerId == "code:ts:apps/web/src/App.tsx:App" &&
            edge.CalleeId ==
            "code:ts:packages/ai-chat-api/src/query-hooks/use-get-conversation-by-id-query.g.ts:useGetConversationById" &&
            edge.EdgeType == EdgeType.Import &&
            edge.Metadata == null);
    }

    private static TypeScriptLanguageExtractor CreateExtractor(
        MockFileSystem fileSystem,
        bool reversePassOrder = false)
    {
        var repositoryWorkspace = new RepositoryWorkspace("/repo", "/test-storage/index.db", fileSystem);
        var tsConfigResolver = new TsConfigResolver(repositoryWorkspace, fileSystem);
        var sourceDiscoverer = new TypeScriptSourceDiscoverer(
            repositoryWorkspace,
            new WorkspaceFileDiscoverer(repositoryWorkspace, fileSystem),
            fileSystem,
            tsConfigResolver);
        var extractionPasses = reversePassOrder
            ? new ITypeScriptExtractionPass[]
            {
                new ImportDependencyPass(tsConfigResolver, repositoryWorkspace),
                new HttpEdgeExtractionPass(),
                new CodeNodeExtractionPass()
            }
            : [
                new CodeNodeExtractionPass(),
                new ImportDependencyPass(tsConfigResolver, repositoryWorkspace),
                new HttpEdgeExtractionPass()
            ];

        return new TypeScriptLanguageExtractor(
            sourceDiscoverer,
            fileSystem,
            extractionPasses);
    }

    private static MockFileSystem CreateRepositoryFileSystem(params (string Path, string Content)[] files)
    {
        var fileData = new Dictionary<string, MockFileData>
        {
            ["/repo/.git/HEAD"] = new("ref: refs/heads/main")
        };

        foreach (var (path, content) in files)
        {
            fileData[path] = new MockFileData(content);
        }

        return new MockFileSystem(fileData, "/repo");
    }
}
