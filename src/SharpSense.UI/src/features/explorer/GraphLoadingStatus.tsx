import { Box, Button, LinearProgress, Stack, Typography } from "@mui/material";
import { ErrorState } from "../../shared/ui/States";
import type { useScopedGraph } from "./useGraphPages";

export function GraphLoadingStatus({
  graph,
  showEdges,
}: {
  graph: ReturnType<typeof useScopedGraph>;
  showEdges: boolean;
}) {
  const totalItems =
    graph.totalNodes === null || (showEdges && graph.totalEdges === null)
      ? null
      : graph.totalNodes + (showEdges ? (graph.totalEdges ?? 0) : 0);
  const loadedItems = graph.loadedNodes + graph.loadedEdges;
  const progress = totalItems
    ? Math.min(100, (100 * loadedItems) / totalItems)
    : null;
  const status = graph.complete
    ? "Loaded"
    : graph.paused
      ? "Paused"
      : graph.error
        ? "Incomplete"
        : "Loading";

  return (
    <Box
      sx={{ px: 1.5, pb: 1.5 }}
      data-testid="graph-loading-state"
      data-complete={graph.complete}
      data-node-count={graph.loadedNodes}
      data-edge-count={graph.loadedEdges}
    >
      <Stack
        direction="row"
        alignItems="center"
        spacing={1}
        justifyContent="space-between"
      >
        <Typography variant="caption" color="text.secondary" aria-live="polite">
          {status} {graph.loadedNodes.toLocaleString()}
          {graph.totalNodes !== null &&
            ` / ${graph.totalNodes.toLocaleString()}`}{" "}
          nodes
          {showEdges &&
            ` · ${graph.loadedEdges.toLocaleString()}${graph.totalEdges === null ? "" : ` / ${graph.totalEdges.toLocaleString()}`} relationships`}
        </Typography>
        {!graph.complete && (
          <Button size="small" onClick={() => graph.setPaused(!graph.paused)}>
            {graph.paused ? "Resume" : "Pause"}
          </Button>
        )}
      </Stack>
      {!graph.complete && !graph.paused && !graph.error && (
        <LinearProgress
          variant={progress === null ? "indeterminate" : "determinate"}
          value={progress ?? undefined}
          sx={{ mt: 0.5 }}
        />
      )}
      {graph.error && graph.loadedNodes > 0 && (
        <Box sx={{ mt: 1 }}>
          <ErrorState error={graph.error} retry={graph.retry} />
        </Box>
      )}
    </Box>
  );
}
