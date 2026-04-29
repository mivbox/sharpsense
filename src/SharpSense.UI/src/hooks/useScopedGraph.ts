import { useQuery } from "@tanstack/react-query";
import { EMPTY_GRAPH, type GraphApiResponse } from "../types/graph";
import type { WorkspaceTreeNode } from "../types/workspaceTree";

const GRAPH_ENDPOINT = "/api/graph";

export function useScopedGraph(
  selectedPaths: string[],
  nodesByPath: Record<string, WorkspaceTreeNode>
) {
  const normalizedSelectedPaths = normalizeSelectedPaths(selectedPaths);
  const selectedDirectoryIds = normalizedSelectedPaths
    .map((path) => nodesByPath[path])
    .filter((node): node is WorkspaceTreeNode => Boolean(node?.isSelectable))
    .map((node) => node.id);
  const query = useQuery({
    queryKey: ["graph", selectedDirectoryIds],
    queryFn: async () => fetchGraph(selectedDirectoryIds),
    enabled: selectedDirectoryIds.length > 0
  });

  return {
    ...query,
    data: query.data ?? EMPTY_GRAPH,
    selectedDirectoryIds,
    normalizedSelectedPaths
  };
}

async function fetchGraph(directoryIds: number[]): Promise<GraphApiResponse> {
  const searchParams = new URLSearchParams();
  directoryIds.forEach((directoryId) => {
    searchParams.append("directoryIds", String(directoryId));
  });

  const response = await fetch(`${GRAPH_ENDPOINT}?${searchParams.toString()}`);
  if (!response.ok) {
    throw new Error(`Graph request failed with ${response.status}.`);
  }

  return (await response.json()) as GraphApiResponse;
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
