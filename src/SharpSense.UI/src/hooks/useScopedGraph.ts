import { useQuery } from "@tanstack/react-query";
import { EMPTY_GRAPH, type GraphApiResponse } from "../types/graph";

const GRAPH_ENDPOINT = "/api/graph";

export function useScopedGraph(selectedPaths: string[]) {
  const normalizedSelectedPaths = normalizeSelectedPaths(selectedPaths);
  const query = useQuery({
    queryKey: ["graph", normalizedSelectedPaths],
    queryFn: async () => fetchGraph(normalizedSelectedPaths),
    enabled: normalizedSelectedPaths.length > 0
  });

  return {
    ...query,
    data: query.data ?? EMPTY_GRAPH,
    normalizedSelectedPaths
  };
}

async function fetchGraph(paths: string[]): Promise<GraphApiResponse> {
  const searchParams = new URLSearchParams();
  paths.forEach((path) => {
    searchParams.append("paths", path);
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
