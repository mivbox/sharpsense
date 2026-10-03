import { useMemo, useState } from "react";
import { useInfiniteQuery } from "@tanstack/react-query";
import { WorkspaceApiError } from "../../shared/api/transport";
import { useWorkspaceApi } from "../../shared/workspace/context";

type ConnectionCursor = { cursor?: string; revision?: string };

export function useNodeConnections(nodeId: string | undefined) {
  const { getGraphNodeConnections } = useWorkspaceApi();
  const [generation, setGeneration] = useState(0);
  const query = useInfiniteQuery({
    queryKey: ["graph", "node-connections", nodeId, generation],
    initialPageParam: {} as ConnectionCursor,
    queryFn: ({ pageParam, signal }) =>
      getGraphNodeConnections(
        nodeId!,
        pageParam.cursor,
        pageParam.revision,
        signal,
      ),
    getNextPageParam: (page): ConnectionCursor | undefined =>
      page.nextCursor
        ? { cursor: page.nextCursor, revision: page.revision }
        : undefined,
    enabled: Boolean(nodeId),
    staleTime: 30_000,
    gcTime: 0,
  });
  const connections = useMemo(
    () => query.data?.pages.flatMap((page) => page.items) ?? [],
    [query.data?.pages],
  );

  const missing =
    query.error instanceof WorkspaceApiError &&
    query.error.responseStatusCode === 404;

  return {
    missing,
    node: missing ? null : (query.data?.pages[0]?.node ?? null),
    connections: missing ? [] : connections,
    totalCount: query.data?.pages[0]?.totalCount ?? null,
    complete: query.isSuccess && !query.hasNextPage,
    pending: Boolean(nodeId) && query.isPending,
    fetching: query.isFetching,
    hasMore: query.hasNextPage,
    error: query.error,
    loadMore: () => query.fetchNextPage({ cancelRefetch: false }),
    // Restart from the first page after a revision conflict; never join peers
    // from two different index snapshots in the same inspector.
    retry: () => setGeneration((current) => current + 1),
  };
}
