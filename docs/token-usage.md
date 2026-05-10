# Token Usage


## Test Objective

The purpose of this A/B test is to objectively measure the performance differences—specifically in token consumption,
traversal depth, and citation accuracy—between native GitHub Copilot capabilities and an MCP-augmented Copilot agent.

By querying a complex architectural workflow (the boot sequence of the CLI application), we aim to evaluate whether
delegating repository exploration to a dedicated semantic index and graph traversal engine is more efficient than
traditional LLM file-reading strategies.

## Configuration Details

To accurately isolate the variables, the exact same prompt was issued in two distinct, isolated chat sessions using the
following configurations:

### Native Copilot – Without Skill

**Setup**: Standard GitHub Copilot behaviour without custom instructions or external tools.
**Mechanism**: The LLM relies on its native workspace search, grep, and direct file-reading capabilities.

**Context Payload**: The LLM reads entire source code files (including using directives, whitespace, and
unrelated methods) directly into its context window to piece together the execution path.

### SharpSense MCP Server

**Setup**: GitHub Copilot augmented with a custom Model Context Protocol (MCP) server (SharpSense) and a strict system
prompt.
**Mechanism**: The LLM is explicitly forbidden from using native file search. It must exclusively navigate the
repository using two custom tools backed by a local SQLite knowledge graph:

1. **semantic_search**: Locates precise structural coordinates (Node IDs) via a hybrid BM25/vector index.
2. **trace_node**: Traverses the directed dependency graph to find upstream callers or downstream callees.

**Context Payload**: The MCP tools do not return raw source code. They return data in a Token-Optimized Output Network
(TOON) format—highly dense, hierarchical directory/file blocks containing node types, precise IDs, and exact
file/line coordinates (e.g., `src/Feature/:` → `  File.cs:` → `    - [M] \`NodeId\` MethodName L10-20`).

## Prompt

I need to understand how our MCP server boots up. Find the McpCommand class and trace its downstream execution path. I
need to know every major infrastructure and feature service it registers during configuration.

## Results

### Native Copilot – Without Skill

Result: McpCommand is the composition root, not the server loop. The actual MCP server becomes active during
host.StartAsync(); McpCommand.Execute() then just waits for shutdown.

Execution path

1. src/SharpSense.Cli/Program.cs creates the Spectre CommandApp and runs it.
2. src/SharpSense.Cli/Program.CommandApp.cs maps mcp to McpCommand.
3. Spectre calls AbstractAsyncCommand.ExecuteAsync() in src/SharpSense.Cli/Shared/AbstractAsyncCommand.cs, which:

- creates Host.CreateApplicationBuilder()
- configures Serilog to ~/.SharpSense/logs/mcp.log
- calls McpCommand.Configure(settings, services)
- builds the host and calls host.StartAsync()

4. McpCommand.Configure() in src/SharpSense.Cli/Mcp/McpCommand.cs registers workspace, feature, persistence, and MCP
   services.
5. On host.StartAsync(), two hosted-service paths matter:

- EfCoreEnsureDatabase creates/migrates the repo SQLite DB
- the MCP SDK stdio hosted service starts McpServer.RunAsync()

6. McpCommand.Execute() only does await host.WaitForShutdownAsync(ct).
7. When stdio closes, the MCP hosted service stops the app, and the command exits.

Services registered by McpCommand.Configure()

┌─────────────────────────────────────────────────────────────────────────────────┬─────────────────────────────────────────────────────┬──────────────────────────────────────────────┐
│ Registration call │ Major services added │ Purpose │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddRepositoryWorkspace(workspacePath)                                           │ Singleton IRepositoryWorkspace ->
│ Resolves repo root from --repo-root or │
│ │ RepositoryWorkspace │ current dir, walks up to .git, and computes │
│ │ │ DB path under ~/.SharpSense/<repoHash>.db │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddHybridSearch()                                                               │ Transient IQueryHandler<
HybridSearchQuery, │ Feature entrypoint for MCP semantic_search │
│ │ HybridSearchResult> -> HybridSearchQueryHandler │ │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddHybridSearchInfrastructure()                                                 │ Singleton IHybridSearchService ->
│ Hybrid BM25 + vector search implementation │
│ │ HybridSearchService │ │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddEmbeddingsInfrastructure()                                                   │ LocalEmbeddingsOptions configured +
validated on │ Loads local ONNX embeddings model used by │
│ │ start, Singleton IEmbeddingGenerator ->             │ hybrid search │
│ │ EmbeddingGenerator │ │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddImpactAnalysis()                                                             │ Transient IQueryHandler<
ImpactAnalysisQuery, │ Feature entrypoint for trace_node with │
│ │ ImpactAnalysisResult> -> ImpactAnalysisQueryHandler │ direction=caller │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddImpactAnalysisInfrastructure()                                               │ Scoped IImpactAnalysisService ->
│ Upstream impact/blast-radius traversal over │
│ │ ImpactAnalysisService │ dependency edges │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddTrace()                                                                      │ Transient IQueryHandler<TraceQuery,
│ Feature entrypoint for trace_node with │
│ │ CodeNodeResult[]> -> TraceQueryHandler │ direction=callee │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddTraceInfrastructure()                                                        │ Scoped ITraceService -> TraceService
│ Downstream callee lookup │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddPersistence()                                                                │ Singleton SqlitePragmaInterceptor, │
Opens repo SQLite DB, loads sqlite-vec, │
│ │ IDbContextFactory<SharpSenseDbContext>, hosted │ enables WAL / busy timeout / foreign keys, │
│ │ service EfCoreEnsureDatabase │ exposes ProjectNodes, CodeNodes, │
│ │ │ DependencyEdges │
├─────────────────────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────┼──────────────────────────────────────────────┤
│ AddMcpServer().WithStdioServerTransport().WithTools<SharpSenseMcpTools>(...)    │ MCP options setup, Singleton
ITransport -> stdio │ Boots the stdio MCP server and exposes │
│ │ transport, McpServer, single-session MCP hosted │ semantic_search / trace_node │
│ │ service, and one McpServerTool singleton per tool │ │
│ │ method │ │
└─────────────────────────────────────────────────────────────────────────────────┴─────────────────────────────────────────────────────┴──────────────────────────────────────────────┘

Request-time downstream path

- semantic_search -> SharpSenseMcpTools.semantic_search() -> HybridSearchQueryHandler -> HybridSearchService ->
  SharpSenseDbContext + IEmbeddingGenerator
- trace_node with caller -> SharpSenseMcpTools.trace_node() -> ImpactAnalysisQueryHandler -> ImpactAnalysisService ->
  SharpSenseDbContext
- trace_node with callee -> SharpSenseMcpTools.trace_node() -> TraceQueryHandler -> TraceService -> SharpSenseDbContext

One important detail: both trace paths first resolve the target node by exact node ID or fully qualified name via
CodeNodeNavigationQueries.FindRootNodeAsync(), and the MCP tool
serializer is customized so TraceDirection is accepted as strings like caller / callee.

Requests 1 Premium
Tokens ↑ 824.9k • ↓ 13.8k • 756.4k (cached) • 8.8k (reasoning)

### SharpSense MCP Server

Main takeaway: McpCommand lives at src/SharpSense.Cli/Mcp/McpCommand.cs, and the real MCP boot happens in host startup,
not in McpCommand.Execute. The CLI maps sharp-sense mcp to this
command, AbstractAsyncCommand.ExecuteAsync builds and starts the host, McpCommand.Configure registers the server and all
dependencies, and Execute then just waits for shutdown (
src/SharpSense.Cli/Program.CommandApp.cs:54-57, src/SharpSense.Cli/Shared/AbstractAsyncCommand.cs:24-45,
src/SharpSense.Cli/Mcp/McpCommand.cs:37-68).

Boot path

1. Program.ConfigureCommandApp registers the mcp verb with AddCommand<McpCommand>("mcp") (
   src/SharpSense.Cli/Program.CommandApp.cs:54-57).
2. AbstractAsyncCommand.ExecuteAsync creates a HostApplicationBuilder, adds Serilog, calls Configure(settings,
   builder.Services), builds the host, and runs host.StartAsync(ct)
   (src/SharpSense.Cli/Shared/AbstractAsyncCommand.cs:24-45).
3. McpCommand.Configure picks workspacePath from --repo-root or Environment.CurrentDirectory, then registers workspace,
   features, infrastructure, persistence, and the MCP stdio server
   (src/SharpSense.Cli/Mcp/McpCommand.cs:42-58).
4. On host start, persistence runs EfCoreEnsureDatabase, which creates the DB directory and applies EF Core migrations
   (src/SharpSense.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs:32-33,37-55).
5. After startup, McpCommand.Execute only does await host.WaitForShutdownAsync(ct); it does not perform any additional
   server initialization
   (src/SharpSense.Cli/Mcp/McpCommand.cs:61-68).

Configured services in McpCommand.Configure

- Workspace infrastructure: AddRepositoryWorkspace(workspacePath) registers singleton IRepositoryWorkspace from
  RepositoryWorkspace.CreateFromWorkingDirectory(...). It walks up to the
  nearest .git directory/file to resolve repo root and stores the SQLite DB at ~/.SharpSense/{repositoryHash}.db
  (src/SharpSense.Infrastructure/Storage/RepositoryWorkspaceServiceCollectionExtensions.cs:8-17,
  src/SharpSense.Infrastructure/Storage/RepositoryWorkspace.cs:20-35,97-112).
- Hybrid search feature: AddHybridSearch() registers transient IQueryHandler<HybridSearchQuery, HybridSearchResult> ->
  HybridSearchQueryHandler
  (src/SharpSense.Application/Features/HybridSearch/HybridSearchServiceCollectionExtensions.cs:12-16).
- Hybrid search infrastructure: AddHybridSearchInfrastructure() registers singleton IHybridSearchService ->
  HybridSearchService, which depends on
  IDbContextFactory<SharpSenseDbContext> and IEmbeddingGenerator (
  src/SharpSense.Infrastructure/HybridSearch/HybridSearchInfrastructureServiceCollectionExtensions.cs:8-12,
  src/SharpSense.Infrastructure/HybridSearch/HybridSearchService.cs:13-16).
- Embeddings infrastructure: AddEmbeddingsInfrastructure() configures LocalEmbeddingsOptions with model/vocab paths
  under AppContext.BaseDirectory/LocalEmbeddingsModel/default,
  validates them on start, and registers singleton IEmbeddingGenerator -> EmbeddingGenerator
  (src/SharpSense.Infrastructure/Embeddings/EmbeddingsInfrastructureServiceCollectionExtensions.cs:13-34).
- Impact analysis feature: AddImpactAnalysis() registers transient IQueryHandler<ImpactAnalysisQuery,
  ImpactAnalysisResult> -> ImpactAnalysisQueryHandler
  (src/SharpSense.Application/Features/ImpactAnalysis/ImpactAnalysisServiceCollectionExtensions.cs:12-16).
- Impact analysis infrastructure: AddImpactAnalysisInfrastructure() registers scoped IImpactAnalysisService ->
  ImpactAnalysisService, which uses IDbContextFactory<SharpSenseDbContext>
  to traverse dependency edges and code nodes (
  src/SharpSense.Infrastructure/ImpactAnalysis/ImpactAnalysisInfrastructureServiceCollectionExtensions.cs:9-13,
  src/SharpSense.Infrastructure/ImpactAnalysis/ImpactAnalysisService.cs:11-21,40-50).
- Trace feature: AddTrace() registers transient IQueryHandler<TraceQuery, CodeNodeResult[]> -> TraceQueryHandler
  (src/SharpSense.Application/Features/Trace/TraceServiceCollectionExtensions.cs:11-16).
- Trace infrastructure: AddTraceInfrastructure() registers scoped ITraceService -> TraceService, which also uses
  IDbContextFactory<SharpSenseDbContext> to read outgoing dependency
  edges (src/SharpSense.Infrastructure/Trace/TraceInfrastructureServiceCollectionExtensions.cs:9-14,
  src/SharpSense.Infrastructure/Trace/TraceService.cs:10-18,25-42).
- Persistence infrastructure: AddPersistence() registers singleton SqlitePragmaInterceptor, an EF Core
  IDbContextFactory<SharpSenseDbContext>, and hosted service EfCoreEnsureDatabase
  (src/SharpSense.Infrastructure/Persistence/PersistenceServiceCollectionExtensions.cs:17-33).
- MCP server: AddMcpServer().WithStdioServerTransport().WithTools<SharpSenseMcpTools>(_toolSerializerOptions) registers
  the stdio MCP server and exposes SharpSenseMcpTools; serializer
  options add string enum support for TraceDirection (src/SharpSense.Cli/Mcp/McpCommand.cs:56-58,71-75,
  src/SharpSense.Cli/Mcp/SharpSenseMcpTools.cs:15-21,58-64).

Request path after boot

- semantic_search resolves IQueryHandler<HybridSearchQuery, HybridSearchResult> and calls Handle(...), which routes to
  HybridSearchQueryHandler and then IHybridSearchService /
  HybridSearchService (src/SharpSense.Cli/Mcp/SharpSenseMcpTools.cs:20-37,
  src/SharpSense.Application/Features/HybridSearch/HybridSearchServiceCollectionExtensions.cs:12-16).
- trace_node resolves either ImpactAnalysisQueryHandler or TraceQueryHandler depending on direction, then dispatches to
  IImpactAnalysisService or ITraceService
  (src/SharpSense.Cli/Mcp/SharpSenseMcpTools.cs:58-103,
  src/SharpSense.Application/Features/ImpactAnalysis/ImpactAnalysisServiceCollectionExtensions.cs:12-16,
  src/SharpSense.Application/Features/Trace/TraceServiceCollectionExtensions.cs:11-16).

So the short version is: sharp-sense mcp -> McpCommand.Configure registers workspace + search/trace/impact +
embeddings + persistence + MCP stdio server -> host.StartAsync() brings it
live -> Execute just waits.

Requests 1 Premium
Tokens ↑ 467.6k • ↓ 13.9k • 442.5k (cached) • 10.0k (reasoning

## Conclusion

Token Efficiency (Quantitative Analysis)
* **Without Skill Input Tokens**: 824.9k
* **With Skill Input Tokens**: 467.6k
* **Net Savings**: 357.3k tokens per request **(~43% reduction)**.

**Context without sharpsense**: 28.3k tokens
**Context with skill and subagents**: 19.1k tokens

Copilot search relies on brute-force file reads, loading the entirety of McpCommand.cs and every subsequently
referenced file (e.g. HybridSearchExtensions.cs, Program.cs) into the LLM's context window. This includes unneeded
elements like using directives, whitespace, and irrelevant methods. By utilising the MCP tools, the system queried the
SQLite semantic index and returned highly dense, hierarchical TOON blocks containing only the specific nodes requested,
radically reducing payload size.
