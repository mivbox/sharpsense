import { useLayoutEffect, useMemo, useSyncExternalStore } from "react";
import { GraphProjection, type GraphProjectionInput } from "./graphProjection";

export function useGraphProjection(scope: string, input: GraphProjectionInput) {
  // Scope changes discard ingestion cursors even when query page references match.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const projection = useMemo(() => new GraphProjection(), [scope]);
  const { nodePages, edgePages, types, search } = input;
  const snapshot = useSyncExternalStore(
    projection.subscribe,
    projection.getSnapshot,
    projection.getSnapshot,
  );

  // Committed inputs only: speculative or abandoned renders cannot advance
  // ingestion cursors or mutate the indexes behind an already-visible snapshot.
  useLayoutEffect(() => {
    projection.update({ nodePages, edgePages, types, search });
  }, [projection, nodePages, edgePages, types, search]);

  return snapshot;
}
