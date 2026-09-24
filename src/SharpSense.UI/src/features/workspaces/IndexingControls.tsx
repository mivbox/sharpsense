import { useEffect, useRef, useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
  LinearProgress,
  Paper,
  Stack,
  Switch,
  Typography,
} from "@mui/material";
import PlayArrowRoundedIcon from "@mui/icons-material/PlayArrowRounded";
import StopRoundedIcon from "@mui/icons-material/StopRounded";
import VisibilityOutlinedIcon from "@mui/icons-material/VisibilityOutlined";
import { useWorkspace } from "../../shared/workspace/context";
import { startIndexing, stopIndexing } from "./api";
import { AnalysisProgress } from "./AnalysisProgress";
import { activeIndexingStates } from "./indexingStatus";
import { useIndexingStatus } from "./useIndexingStatus";

export function IndexingControls({ onIndexed }: { onIndexed: () => void }) {
  const { workspace } = useWorkspace();
  const client = useQueryClient();
  const [embeddings, setEmbeddings] = useState(true);
  const lastRevision = useRef("");
  const { status, connected, updateStatus } = useIndexingStatus(workspace.id);
  const revision = status.data?.revision ?? 0;
  const streamId = status.data?.streamId;
  useEffect(() => {
    const identity = `${streamId ?? ""}:${revision}`;
    if (identity === lastRevision.current) return;
    lastRevision.current = identity;
    if (revision === 0) return;
    void client.invalidateQueries({
      predicate: (query) => query.queryKey[0] !== "indexing",
    });
    onIndexed();
  }, [revision, streamId, client, onIndexed]);
  const start = useMutation({
    mutationFn: (watch: boolean) =>
      startIndexing(workspace.id, watch, !embeddings),
    onSuccess: updateStatus,
  });
  const stop = useMutation({
    mutationFn: () => stopIndexing(workspace.id),
    onSuccess: updateStatus,
  });
  const state = status.data?.state ?? "idle";
  const active = activeIndexingStates.has(state);
  const pending = start.isPending || stop.isPending || state === "stopping";
  const error = start.error ?? stop.error ?? (!connected ? status.error : null);
  const diagnostics = status.data?.diagnostics ?? [];
  const labels: Record<string, string> = {
    idle: "Ready",
    indexing: "Indexing",
    watching: "Watching",
    stopping: "Stopping",
    stopped: "Stopped",
    completed: "Analysis complete",
    failed: "Indexing failed",
  };

  return (
    <Paper variant="outlined" sx={{ mb: 3, overflow: "hidden" }}>
      <Stack spacing={1.5} sx={{ p: 2 }}>
        <Stack
          direction={{ xs: "column", lg: "row" }}
          spacing={2}
          justifyContent="space-between"
          alignItems={{ lg: "center" }}
        >
          <Stack direction="row" spacing={1.5} alignItems="center">
            <Chip
              size="small"
              label={labels[state] ?? state}
              color={
                state === "failed"
                  ? "error"
                  : state === "watching"
                    ? "success"
                    : "default"
              }
            />
            <Typography variant="body2" color="text.secondary">
              {status.data?.message ??
                "Analyze your selected sources to build the graph."}
            </Typography>
          </Stack>
          <Stack
            direction="row"
            spacing={1}
            alignItems="center"
            flexWrap="wrap"
            useFlexGap
          >
            {!active && (
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={embeddings}
                    disabled={pending}
                    onChange={(_, checked) => setEmbeddings(checked)}
                  />
                }
                label={<Typography variant="body2">Semantic search</Typography>}
              />
            )}
            {active ? (
              <Button
                startIcon={<StopRoundedIcon />}
                disabled={pending}
                onClick={() => stop.mutate()}
              >
                {state === "stopping" ? "Stopping…" : "Stop"}
              </Button>
            ) : (
              <>
                <Button
                  startIcon={<PlayArrowRoundedIcon />}
                  disabled={pending || workspace.sources.length === 0}
                  onClick={() => start.mutate(false)}
                >
                  Analyze
                </Button>
                <Button
                  variant="contained"
                  startIcon={<VisibilityOutlinedIcon />}
                  disabled={pending || workspace.sources.length === 0}
                  onClick={() => start.mutate(true)}
                >
                  Analyze &amp; watch
                </Button>
              </>
            )}
          </Stack>
        </Stack>
        {!connected && active && (
          <Typography variant="caption" color="text.secondary" role="status">
            Reconnecting to live updates. Checking analysis status periodically.
          </Typography>
        )}
        {status.data?.analysis && (
          <AnalysisProgress analysis={status.data.analysis} />
        )}
        {error && <Alert severity="error">{error.message}</Alert>}
        {state === "failed" && (
          <Alert severity="error">
            {diagnostics.slice(0, 3).join(" ") ||
              status.data?.message ||
              "Indexing failed. The previous graph is preserved."}
          </Alert>
        )}
        {state !== "failed" && diagnostics.length > 0 && (
          <Alert severity="warning">
            {diagnostics.slice(0, 3).join(" ")}
            {diagnostics.length > 3 &&
              ` Plus ${diagnostics.length - 3} more diagnostics. View index status for details.`}
          </Alert>
        )}
        {state === "watching" && (
          <Box>
            <Typography variant="caption" color="text.secondary">
              Changes to this workspace’s selected sources will refresh its
              graph automatically.
            </Typography>
          </Box>
        )}
      </Stack>
      {(state === "indexing" ||
        status.data?.analysis?.state === "running" ||
        pending) && <LinearProgress aria-label="Analysis activity" />}
    </Paper>
  );
}
