import { useQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import { EMPTY_GRAPH, type GraphApiResponse } from "../types/graph";
import type { WorkspaceTreeNode } from "../types/workspaceTree";

const GRAPH_NODES_ENDPOINT = "/api/graph/nodes";
const GRAPH_EDGES_ENDPOINT = "/api/graph/edges";

export function useScopedGraph(
  selectedPaths: string[],
  nodesByPath: Record<string, WorkspaceTreeNode>,
  showEdges: boolean
) {
  const normalizedSelectedPaths = normalizeSelectedPaths(selectedPaths);
  const selectedDirectoryIds = normalizedSelectedPaths
    .map((path) => nodesByPath[path])
    .filter((node): node is WorkspaceTreeNode => Boolean(node?.isSelectable))
    .map((node) => node.id);

  const nodesQuery = useQuery({
    queryKey: ["graph", "nodes", selectedDirectoryIds],
    queryFn: async () => fetchGraphNodes(selectedDirectoryIds),
    enabled: selectedDirectoryIds.length > 0
  });
  const edgesQuery = useQuery({
    queryKey: ["graph", "edges", selectedDirectoryIds],
    queryFn: async () => fetchGraphEdges(selectedDirectoryIds),
    enabled: showEdges && nodesQuery.isSuccess
  });

  const data = useMemo<GraphApiResponse>(
    () => ({
      nodes: nodesQuery.data ?? EMPTY_GRAPH.nodes,
      edges: showEdges ? edgesQuery.data ?? EMPTY_GRAPH.edges : EMPTY_GRAPH.edges
    }),
    [edgesQuery.data, nodesQuery.data, showEdges]
  );
  const error = nodesQuery.error ?? (showEdges ? edgesQuery.error : null);
  const isLoading = nodesQuery.isLoading || (nodesQuery.isFetching && !nodesQuery.data);
  const isFetching =
    nodesQuery.isFetching || (showEdges && nodesQuery.isSuccess && edgesQuery.isFetching);

  return {
    data,
    error,
    isFetching,
    isLoading,
    selectedDirectoryIds,
    normalizedSelectedPaths
  };
}

async function fetchGraphNodes(directoryIds: number[]): Promise<GraphApiResponse["nodes"]> {
  return fetchGraphCollection<GraphApiResponse["nodes"]>(GRAPH_NODES_ENDPOINT, directoryIds);
}

async function fetchGraphEdges(directoryIds: number[]): Promise<GraphApiResponse["edges"]> {
  return fetchGraphCollection<GraphApiResponse["edges"]>(GRAPH_EDGES_ENDPOINT, directoryIds);
}

async function fetchGraphCollection<T>(
  endpoint: string,
  directoryIds: number[]
): Promise<T> {
  const searchParams = new URLSearchParams();
  directoryIds.forEach((directoryId) => {
    searchParams.append("directoryIds", String(directoryId));
  });

  const response = await fetch(`${endpoint}?${searchParams.toString()}`);
  if (!response.ok) {
    throw new Error(`Graph request failed with ${response.status}.`);
  }

  return (await response.json()) as T;
}

export function normalizeSelectedPaths(paths: string[]): string[] {
  const sortedPaths = [...new Set(paths.map((path) => normalizePath(path)).filter(Boolean))].sort();
  const normalizedPaths: string[] = [];

  sortedPaths.forEach((path) => {
    const hasSelectedAncestor = normalizedPaths.some((selectedPath) =>
      isSamePathOrAncestor(selectedPath, path)
    );
    if (!hasSelectedAncestor) {
      normalizedPaths.push(path);
    }
  });

  return normalizedPaths;
}

function isSamePathOrAncestor(ancestorPath: string, candidatePath: string): boolean {
  return ancestorPath === candidatePath || candidatePath.startsWith(`${ancestorPath}/`);
}

function normalizePath(path: string): string {
  return path.trim().replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
}
