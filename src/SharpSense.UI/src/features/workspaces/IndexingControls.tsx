import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
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
import { getIndexingStatus, startIndexing, stopIndexing } from "./api";

const activeStates = new Set(["indexing", "watching", "stopping"]);

export function IndexingControls({ onIndexed }: { onIndexed: () => void }) {
  const { workspace } = useWorkspace();
  const client = useQueryClient();
  const [embeddings, setEmbeddings] = useState(true);
  const lastRevision = useRef(0);
  const status = useQuery({
    queryKey: ["indexing", workspace.id],
    queryFn: ({ signal }) => getIndexingStatus(workspace.id, signal),
    refetchInterval: (query) =>
      activeStates.has(query.state.data?.state ?? "") ? 1500 : 5000,
    staleTime: 0,
  });
  const revision = status.data?.revision ?? 0;
  useEffect(() => {
    if (revision === lastRevision.current) return;
    lastRevision.current = revision;
    if (revision === 0) return;
    void client.invalidateQueries({
      predicate: (query) => query.queryKey[0] !== "indexing",
    });
    onIndexed();
  }, [revision, client, onIndexed]);
  const start = useMutation({
    mutationFn: (watch: boolean) =>
      startIndexing(workspace.id, watch, !embeddings),
    onSuccess: (value) =>
      client.setQueryData(["indexing", workspace.id], value),
  });
  const stop = useMutation({
    mutationFn: () => stopIndexing(workspace.id),
    onSuccess: (value) =>
      client.setQueryData(["indexing", workspace.id], value),
  });
  const state = status.data?.state ?? "idle";
  const active = activeStates.has(state);
  const pending = start.isPending || stop.isPending || state === "stopping";
  const error = start.error ?? stop.error ?? status.error;
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
                Stop
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
        {error && <Alert severity="error">{error.message}</Alert>}
        {state === "failed" && (
          <Alert severity="error">
            {status.data?.diagnostics?.join(" ") ||
              status.data?.message ||
              "Indexing failed. The previous graph is preserved."}
          </Alert>
        )}
        {state === "watching" && (
          <Box>
            {Boolean(status.data?.diagnostics?.length) && (
              <Alert severity="warning" sx={{ mb: 1 }}>
                {status.data?.diagnostics?.slice(0, 3).join(" ")}
              </Alert>
            )}
            <Typography variant="caption" color="text.secondary">
              Changes to this workspace’s selected sources will refresh its
              graph automatically.
            </Typography>
          </Box>
        )}
      </Stack>
      {(state === "indexing" || pending) && <LinearProgress />}
    </Paper>
  );
}
