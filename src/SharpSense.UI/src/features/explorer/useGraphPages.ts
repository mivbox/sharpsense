import { useEffect, useMemo, useState } from "react";
import { useInfiniteQuery, useQueryClient } from "@tanstack/react-query";
import type { GraphEdge, GraphNode, GraphPage } from "../../shared/api/models";
import { useWorkspaceApi } from "../../shared/workspace/context";

const EMPTY_NODE_PAGES: GraphPage<GraphNode>[] = [];
const EMPTY_EDGE_PAGES: GraphPage<GraphEdge>[] = [];

type PageCursor = { cursor?: string; revision?: string };

export function useScopedGraph(directoryIds: number[], showEdges: boolean) {
  const { getGraphNodesPage, getGraphEdgesPage } = useWorkspaceApi();
  const queryClient = useQueryClient();
  const scopeKey = [...new Set(directoryIds)].sort((a, b) => a - b).join(",");
  const ids = useMemo(
    () => (scopeKey ? scopeKey.split(",").map(Number) : []),
    [scopeKey],
  );
  const [generation, setGeneration] = useState(0);
  const [pauseState, setPauseState] = useState({ scope: "", paused: false });
  const paused = pauseState.scope === scopeKey && pauseState.paused;
  const nodeKey = useMemo(
    () => ["graph", "pages", scopeKey, generation, "nodes"] as const,
    [scopeKey, generation],
  );

  const nodes = useInfiniteQuery({
    queryKey: nodeKey,
    initialPageParam: {} as PageCursor,
    queryFn: ({ pageParam, signal }) =>
      getGraphNodesPage(ids, pageParam.cursor, pageParam.revision, signal),
    getNextPageParam: (page): PageCursor | undefined =>
      page.nextCursor
        ? { cursor: page.nextCursor, revision: page.revision }
        : undefined,
    enabled: ids.length > 0 && !paused,
    staleTime: Infinity,
    // Large graphs belong to the current explorer. Release them when leaving
    // the scope instead of retaining several complete graphs in the cache.
    gcTime: 0,
    structuralSharing: false,
  });
  const revision = nodes.data?.pages[0]?.revision;
  const edgeKey = useMemo(
    () => ["graph", "pages", scopeKey, generation, "edges", revision] as const,
    [scopeKey, generation, revision],
  );
  const edges = useInfiniteQuery({
    queryKey: edgeKey,
    initialPageParam: undefined as string | undefined,
    queryFn: ({ pageParam, signal }) =>
      getGraphEdgesPage(ids, revision!, pageParam, signal),
    getNextPageParam: (page) => page.nextCursor ?? undefined,
    enabled: ids.length > 0 && Boolean(revision) && showEdges && !paused,
    staleTime: Infinity,
    gcTime: 0,
    structuralSharing: false,
  });
  const fetchNextNodesPage = nodes.fetchNextPage;
  const fetchNextEdgesPage = edges.fetchNextPage;

  useEffect(() => {
    if (paused || nodes.isFetching || nodes.isError || !nodes.hasNextPage)
      return;
    const timer = window.setTimeout(() => {
      void fetchNextNodesPage({ cancelRefetch: false });
    }, 16);
    return () => window.clearTimeout(timer);
  }, [
    paused,
    nodes.isFetching,
    nodes.isError,
    nodes.hasNextPage,
    fetchNextNodesPage,
  ]);

  useEffect(() => {
    if (
      !showEdges ||
      paused ||
      edges.isFetching ||
      edges.isError ||
      !edges.hasNextPage
    )
      return;
    const timer = window.setTimeout(() => {
      void fetchNextEdgesPage({ cancelRefetch: false });
    }, 16);
    return () => window.clearTimeout(timer);
  }, [
    showEdges,
    paused,
    edges.isFetching,
    edges.isError,
    edges.hasNextPage,
    fetchNextEdgesPage,
  ]);

  useEffect(() => {
    if (!showEdges)
      void queryClient.cancelQueries({ queryKey: edgeKey, exact: true });
  }, [showEdges, queryClient, edgeKey]);

  const nodePages = nodes.data?.pages ?? EMPTY_NODE_PAGES;
  const edgePages = showEdges
    ? (edges.data?.pages ?? EMPTY_EDGE_PAGES)
    : EMPTY_EDGE_PAGES;
  const loadedNodes = useMemo(
    () => nodePages.reduce((count, page) => count + page.items.length, 0),
    [nodePages],
  );
  const loadedEdges = useMemo(
    () => edgePages.reduce((count, page) => count + page.items.length, 0),
    [edgePages],
  );

  const setPaused = (value: boolean) => {
    setPauseState({ scope: scopeKey, paused: value });
    if (value) {
      void queryClient.cancelQueries({ queryKey: nodeKey, exact: true });
      void queryClient.cancelQueries({ queryKey: edgeKey, exact: true });
    }
  };

  return {
    scope: JSON.stringify([scopeKey, generation, revision]),
    nodePages,
    edgePages,
    loadedNodes,
    loadedEdges,
    pending: ids.length > 0 && nodes.isPending,
    fetching: nodes.isFetching || (showEdges && edges.isFetching),
    complete:
      ids.length > 0 &&
      nodes.isSuccess &&
      !nodes.hasNextPage &&
      (!showEdges || (edges.isSuccess && !edges.hasNextPage)),
    totalNodes: nodes.data?.pages[0]?.totalCount ?? null,
    totalEdges: edges.data?.pages[0]?.totalCount ?? null,
    error: nodes.error ?? (showEdges ? edges.error : null),
    paused,
    setPaused,
    retry: () => {
      setPauseState({ scope: scopeKey, paused: false });
      setGeneration((current) => current + 1);
    },
  };
}
