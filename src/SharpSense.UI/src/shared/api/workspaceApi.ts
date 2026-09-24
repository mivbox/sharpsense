import { createApiClient } from "./client";
import {
  requestConfiguration as cancellationConfiguration,
  workspaceRequest as request,
} from "./transport";
import type {
  MemoryNode as ApiMemory,
  GraphConnectionNode as ApiGraphConnectionNode,
  GraphNodeRelationship as ApiGraphNodeRelationship,
  CodeNodeResult,
  Context360Result,
  GraphStatsSnapshot,
  ImpactAnalysisResult,
  TraceResponse,
} from "./generated/models";
import type {
  GraphEdge,
  GraphNode,
  GraphNodeSummary,
  GraphNodeConnectionsPage,
  GraphRelationship,
  GraphPage,
  Memory,
  MemoryIntent,
  SearchHit,
  TreeResult,
  WorkspaceOverview,
} from "./models";

function present<T>(value: T | null | undefined): T {
  if (value == null)
    throw new Error(
      "The workspace returned an empty response. Refresh and try again.",
    );
  return value;
}
function id(value: number | null | undefined): number {
  if (!Number.isInteger(value) || value! <= 0)
    throw new Error("The workspace returned an invalid symbol identifier.");
  return value!;
}
function memory(value: ApiMemory): Memory {
  return {
    id: present(value.id),
    targetFullyQualifiedName: value.targetFullyQualifiedName ?? "",
    content: value.content ?? undefined,
    intent: value.intent ?? "Convention",
    tags: value.tags ?? [],
    isStale: value.isStale ?? false,
    createdAt: value.createdAt?.toISOString(),
  };
}
function graphNodeSummary(value: ApiGraphConnectionNode): GraphNodeSummary {
  return {
    id: String(id(value.id)),
    codeNodeId: value.codeNodeId ?? null,
    label: present(value.label),
    type: present(value.type),
    relativePath: value.relativePath ?? null,
    projectId: value.projectId == null ? null : String(id(value.projectId)),
  };
}
function graphRelationship(value: ApiGraphNodeRelationship): GraphRelationship {
  const direction = value.direction;
  if (
    direction !== "incoming" &&
    direction !== "outgoing" &&
    direction !== "self"
  )
    throw new Error(
      "The workspace returned an invalid relationship direction.",
    );
  return {
    type: present(value.type),
    direction,
    metadata: value.metadata ?? null,
  };
}
export function createWorkspaceApi(workspaceId: string) {
  const apiClient = createApiClient();
  const requestConfiguration = (signal?: AbortSignal) => ({
    ...cancellationConfiguration(signal),
    headers: { "X-SharpSense-Workspace": workspaceId },
  });
  async function getOverview(signal?: AbortSignal): Promise<WorkspaceOverview> {
    const value = present(
      await request(
        () => apiClient.api.overview.get(requestConfiguration(signal)),
        signal,
      ),
    );
    return {
      id: value.workspaceId ?? null,
      name: value.name ?? "Workspace",
      root: value.repositoryRoot ?? "",
      sources: (value.sources ?? []).map((source) => ({
        kind: source.kind ?? "Unknown",
        path: present(source.path),
      })),
      projects: value.projectCount ?? 0,
      files: value.documentCount ?? 0,
      nodes: value.nodeCount ?? 0,
      edges: value.edgeCount ?? 0,
      memories: value.memoryCount ?? 0,
      isIndexed: value.indexed ?? false,
    };
  }
  async function getTree(
    path: string,
    signal?: AbortSignal,
  ): Promise<TreeResult> {
    const value = present(
      await request(
        () =>
          apiClient.api.tree.get({
            ...requestConfiguration(signal),
            queryParameters: { path },
          }),
        signal,
      ),
    );
    return {
      parentPath: value.parentPath ?? path,
      parentDirectoryId: value.parentDirectoryId ?? null,
      nodes: (value.nodes ?? []).map((node) => ({
        id: id(node.id),
        parentId: node.parentId ?? null,
        path: present(node.path),
        label: node.label ?? node.path ?? "",
        kind:
          node.kind === "project" || node.kind === "file"
            ? node.kind
            : "folder",
        hasChildren: node.hasChildren ?? false,
        childCount: node.childCount ?? null,
        isSelectable: node.isSelectable ?? false,
      })),
    };
  }
  async function getGraphNodesPage(
    directoryIds: number[],
    cursor?: string,
    revision?: string,
    signal?: AbortSignal,
  ): Promise<GraphPage<GraphNode>> {
    const value = present(
      await request(
        () =>
          apiClient.api.graph.nodes.page.get({
            ...requestConfiguration(signal),
            queryParameters: {
              directoryIds,
              cursor,
              revision,
              pageSize: cursor ? 5000 : 2000,
              includeTotal: !cursor,
            },
          }),
        signal,
      ),
    );
    return {
      revision: present(value.revision),
      nextCursor: value.nextCursor ?? null,
      totalCount: value.totalCount ?? null,
      items: (value.items ?? []).map((node) => ({
        id: String(id(node.id)),
        codeNodeId: node.codeNodeId ?? null,
        label: node.label ?? String(node.id),
        type: (node.type ?? "symbol").toLowerCase(),
        relativePath: node.relativePath ?? null,
        projectId: node.projectId == null ? null : String(node.projectId),
        scope: node.scope === "external" ? "external" : "selected",
        isClickable: node.isClickable ?? false,
      })),
    };
  }
  async function getGraphEdgesPage(
    directoryIds: number[],
    revision: string,
    cursor?: string,
    signal?: AbortSignal,
  ): Promise<GraphPage<GraphEdge>> {
    const value = present(
      await request(
        () =>
          apiClient.api.graph.edges.page.get({
            ...requestConfiguration(signal),
            queryParameters: {
              directoryIds,
              cursor,
              revision,
              pageSize: 5000,
              includeTotal: !cursor,
            },
          }),
        signal,
      ),
    );
    return {
      revision: present(value.revision),
      nextCursor: value.nextCursor ?? null,
      totalCount: value.totalCount ?? null,
      items: (value.items ?? []).map((edge) => ({
        id: `${id(edge.source)}|${id(edge.target)}|${edge.type ?? "dependency"}`,
        source: String(id(edge.source)),
        target: String(id(edge.target)),
        type: edge.type ?? "dependency",
        scope: edge.scope === "boundary" ? "boundary" : "internal",
        metadata: edge.metadata ?? null,
      })),
    };
  }
  async function getGraphNodeConnections(
    nodeId: string,
    cursor?: string,
    revision?: string,
    signal?: AbortSignal,
  ): Promise<GraphNodeConnectionsPage> {
    const value = present(
      await request(
        () =>
          apiClient.api.graph.nodes
            .byNodeId(id(Number(nodeId)))
            .connections.get({
              ...requestConfiguration(signal),
              queryParameters: {
                cursor,
                revision,
                pageSize: 20,
                includeTotal: !cursor,
              },
            }),
        signal,
      ),
    );
    return {
      node: graphNodeSummary(present(value.node)),
      revision: present(value.revision),
      nextCursor: value.nextCursor ?? null,
      totalCount: value.totalCount ?? null,
      items: (value.items ?? []).map((connection) => ({
        node: graphNodeSummary(present(connection.node)),
        relationships: (connection.relationships ?? []).map(graphRelationship),
      })),
    };
  }
  async function getNodeMemories(
    nodeId: number,
    signal?: AbortSignal,
  ): Promise<Memory[]> {
    return (
      (await request(
        () =>
          apiClient.api.memory.node
            .byNodeId(nodeId)
            .get(requestConfiguration(signal)),
        signal,
      )) ?? []
    ).map(memory);
  }
  async function getMemory(
    memoryId: string,
    signal?: AbortSignal,
  ): Promise<Memory> {
    return memory(
      present(
        await request(
          () =>
            apiClient.api.memory
              .byMemoryId(memoryId)
              .get(requestConfiguration(signal)),
          signal,
        ),
      ),
    );
  }
  async function addMemory(
    nodeId: number,
    payload: { content: string; tags: string[]; intent: MemoryIntent },
  ): Promise<void> {
    await request(() =>
      apiClient.api.memory.node
        .byNodeId(nodeId)
        .post(payload, requestConfiguration()),
    );
  }
  async function deleteMemory(memoryId: string): Promise<void> {
    await request(() =>
      apiClient.api.memory.byMemoryId(memoryId).delete(requestConfiguration()),
    );
  }
  async function searchWorkspace(
    query: string,
    limit: number,
    signal?: AbortSignal,
  ): Promise<SearchHit[]> {
    const result = present(
      await request(
        () =>
          apiClient.api.tools.search.post(
            { query, limit },
            requestConfiguration(signal),
          ),
        signal,
      ),
    );
    return (result.hits ?? []).map((hit) => ({
      nodeId: id(hit.id),
      label: hit.fullyQualifiedName ?? hit.displayName ?? "Unnamed symbol",
      kind: hit.nodeType ?? "Symbol",
      path: hit.relativeFilePath ?? "",
      summary: hit.summary ?? "",
      startLine: hit.startLine ?? undefined,
      endLine: hit.endLine ?? undefined,
    }));
  }
  async function getTools(
    signal?: AbortSignal,
  ): Promise<
    { id: string; name: string; description: string; requiresNode: boolean }[]
  > {
    return (
      (await request(
        () => apiClient.api.tools.get(requestConfiguration(signal)),
        signal,
      )) ?? []
    ).map((tool) => ({
      id: present(tool.id),
      name: tool.name ?? tool.id ?? "",
      description: tool.description ?? "",
      requiresNode: tool.requiresNode ?? true,
    }));
  }
  async function getGraphStats(
    signal?: AbortSignal,
  ): Promise<GraphStatsSnapshot> {
    return present(
      await request(
        () => apiClient.api.tools.graphStats.get(requestConfiguration(signal)),
        signal,
      ),
    );
  }
  async function getContext(
    nodeId: number,
    maxRelated: number,
    signal?: AbortSignal,
  ): Promise<Context360Result> {
    return present(
      await request(
        () =>
          apiClient.api.tools.context.post(
            { nodeId: id(nodeId), maxRelated },
            requestConfiguration(signal),
          ),
        signal,
      ),
    );
  }
  async function traceNode(
    nodeId: number,
    direction: "caller" | "callee",
    maxDepth: number,
    signal?: AbortSignal,
  ): Promise<TraceResponse> {
    return present(
      await request(
        () =>
          apiClient.api.tools.tracePath.post(
            { nodeId: id(nodeId), direction, maxDepth },
            requestConfiguration(signal),
          ),
        signal,
      ),
    );
  }
  async function getInheritors(
    nodeId: number,
    signal?: AbortSignal,
  ): Promise<CodeNodeResult[]> {
    return present(
      await request(
        () =>
          apiClient.api.tools.inheritors.post(
            { nodeId: id(nodeId) },
            requestConfiguration(signal),
          ),
        signal,
      ),
    );
  }
  async function getImpact(
    nodeId: number,
    maxDepth: number,
    signal?: AbortSignal,
  ): Promise<ImpactAnalysisResult> {
    return present(
      await request(
        () =>
          apiClient.api.tools.impact.post(
            { nodeId: id(nodeId), maxDepth },
            requestConfiguration(signal),
          ),
        signal,
      ),
    );
  }

  return {
    getOverview,
    getTree,
    getGraphNodesPage,
    getGraphEdgesPage,
    getGraphNodeConnections,
    getNodeMemories,
    getMemory,
    addMemory,
    deleteMemory,
    searchWorkspace,
    getTools,
    getGraphStats,
    getContext,
    traceNode,
    getInheritors,
    getImpact,
  };
}

export type WorkspaceApi = ReturnType<typeof createWorkspaceApi>;
