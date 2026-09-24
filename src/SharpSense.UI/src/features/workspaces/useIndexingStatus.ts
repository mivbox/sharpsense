import { useEffect, useMemo, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "../../shared/api/client";
import type { WorkspaceIndexingStatus } from "../../shared/api/generated/models";
import { getIndexingStatus } from "./api";
import { subscribeToIndexing } from "./indexingEvents";
import { activeIndexingStates, IndexingStatusOrder } from "./indexingStatus";

export function useIndexingStatus(workspaceId: string) {
  const client = useQueryClient();
  const [connection, setConnection] = useState<string>();
  const connected = connection === workspaceId;
  const queryKey = useMemo(() => ["indexing", workspaceId], [workspaceId]);
  const order = useMemo(
    () => new IndexingStatusOrder(workspaceId),
    [workspaceId],
  );
  const status = useQuery({
    queryKey,
    queryFn: ({ signal }) => getIndexingStatus(workspaceId, signal),
    structuralSharing: (_previous, incoming) =>
      order.accept(incoming as WorkspaceIndexingStatus | undefined),
    refetchInterval: (query) =>
      connected
        ? false
        : activeIndexingStates.has(query.state.data?.state ?? "")
          ? 1500
          : 5000,
    refetchOnWindowFocus: !connected,
    refetchOnReconnect: !connected,
    staleTime: connected ? Infinity : 0,
  });

  useEffect(() => {
    const url = apiClient.api.workspaces
      .byWorkspaceId(workspaceId)
      .indexing.events.toGetRequestInformation().URL;
    return subscribeToIndexing(
      url,
      workspaceId,
      (value) => client.setQueryData(queryKey, order.accept(value, true)),
      (value) => setConnection(value ? workspaceId : undefined),
    );
  }, [client, order, queryKey, workspaceId]);

  function updateStatus(value: WorkspaceIndexingStatus | undefined) {
    client.setQueryData(queryKey, order.accept(value));
  }

  return { status, connected, updateStatus };
}
