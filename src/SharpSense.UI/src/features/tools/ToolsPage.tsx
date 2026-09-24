import { useEffect, useRef, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Button,
  Chip,
  Divider,
  InputAdornment,
  List,
  ListItemButton,
  ListItemText,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
} from "@mui/material";
import PlayArrowRoundedIcon from "@mui/icons-material/PlayArrowRounded";
import SearchRoundedIcon from "@mui/icons-material/SearchRounded";
import TerminalRoundedIcon from "@mui/icons-material/TerminalRounded";
import { useWorkspaceApi } from "../../shared/workspace/context";
import type { ToolId, ToolInput } from "../../shared/api/models";
import type { ToolsSearch } from "../../app/searchState";
import { useWorkspaceTools } from "../../shared/api/queries";
import { ErrorState } from "../../shared/ui/States";
import { toolDefinitions } from "./toolDefinitions";
import { ToolResultView } from "./ToolResultView";
import { executeTool } from "./api";

export default function ToolsPage({
  state,
  onStateChange,
  onSearch,
}: {
  state: ToolsSearch;
  onStateChange: (patch: Partial<ToolsSearch>, replace?: boolean) => void;
  onSearch: () => void;
}) {
  const api = useWorkspaceApi();
  const activeRequest = useRef<AbortController | null>(null);
  useEffect(() => () => activeRequest.current?.abort(), []);
  const tool = state.tool;
  const [draft, setDraft] = useState({
    source: state.nodeId,
    value: state.nodeId ? String(state.nodeId) : "",
  });
  if (draft.source !== state.nodeId) {
    setDraft({
      source: state.nodeId,
      value: state.nodeId ? String(state.nodeId) : "",
    });
  }
  const nodeId = draft.value;
  const setNodeId = (value: string) =>
    setDraft({ source: state.nodeId, value });
  const direction = state.direction;
  const depth = state.depth;
  const maxRelated = state.maxRelated;
  const setDirection = (value: "caller" | "callee") =>
    onStateChange({ direction: value });
  const setDepth = (value: number) => onStateChange({ depth: value });
  const setMaxRelated = (value: number) => onStateChange({ maxRelated: value });
  const available = useWorkspaceTools();
  const run = useMutation({
    mutationFn: (request: {
      tool: ToolId;
      input: ToolInput;
      parameterKey: string;
    }) => {
      activeRequest.current?.abort();
      const controller = new AbortController();
      activeRequest.current = controller;
      return executeTool(api, request.tool, request.input, controller.signal);
    },
  });
  const definition = toolDefinitions.find((item) => item.id === tool)!;
  const requiresNode = tool !== "graph_stats";
  const numericId = Number(nodeId);
  const valid = !requiresNode || (Number.isInteger(numericId) && numericId > 0);
  const selectedLabel = numericId === state.nodeId ? state.label : undefined;
  const parameterKey = JSON.stringify(
    requiresNode ? [tool, numericId, direction, depth, maxRelated] : [tool],
  );
  const matchesCurrentParameters = run.variables?.parameterKey === parameterKey;
  const submit = () => {
    if (!valid) return;
    run.mutate({
      parameterKey,
      tool,
      input: {
        ...(requiresNode ? { nodeId: numericId } : {}),
        ...(tool === "trace"
          ? { direction, maxDepth: depth }
          : tool === "impact"
            ? { maxDepth: depth }
            : tool === "context"
              ? { maxRelated }
              : {}),
      },
    });
  };
  const changeTool = (next: ToolId) => {
    onStateChange({ tool: next });
    run.reset();
  };
  const changeNode = (value: string) => {
    setNodeId(value);
    const parsed = Number(value);
    if (Number.isInteger(parsed) && parsed > 0)
      onStateChange({ nodeId: parsed, label: undefined }, true);
    run.reset();
  };
  return (
    <Stack spacing={2}>
      {available.error && (
        <ErrorState
          error={available.error}
          retry={() => {
            void available.refetch();
          }}
        />
      )}
      <Box
        sx={{
          display: "grid",
          gridTemplateColumns: { xs: "1fr", lg: "270px minmax(0, 1fr)" },
          gap: 2.5,
        }}
      >
        <Stack spacing={2}>
          <Paper variant="outlined">
            <Box sx={{ p: 2.5 }}>
              <Typography variant="subtitle1">Explore tools</Typography>
              <Typography variant="caption" color="text.secondary">
                Read from your local code index.
              </Typography>
            </Box>
            <Divider />
            <List disablePadding>
              {toolDefinitions.map((item) => (
                <ListItemButton
                  key={item.id}
                  selected={tool === item.id}
                  onClick={() => changeTool(item.id)}
                  disabled={
                    run.isPending ||
                    (available.isSuccess &&
                      !available.data.some((entry) => entry.id === item.id))
                  }
                  data-testid={"tool-" + item.id}
                  sx={{ py: 1.75 }}
                >
                  <ListItemText
                    primary={item.label}
                    secondary={item.description}
                    primaryTypographyProps={{ variant: "subtitle2" }}
                    secondaryTypographyProps={{ variant: "caption" }}
                  />
                </ListItemButton>
              ))}
            </List>
          </Paper>
          <Alert severity="info">
            {requiresNode
              ? "Choose a symbol in Explorer or Search to prefill its node ID. Tools only run when you choose Run."
              : "Graph statistics reads the whole workspace. Choose Run to take a fresh snapshot of the index."}
          </Alert>
          {requiresNode && (
            <Button
              variant="outlined"
              startIcon={<SearchRoundedIcon />}
              onClick={onSearch}
            >
              Find a symbol
            </Button>
          )}
        </Stack>
        <Stack spacing={2.5}>
          <Paper
            variant="outlined"
            component="form"
            onSubmit={(event) => {
              event.preventDefault();
              submit();
            }}
            sx={{ p: { xs: 2, sm: 3 } }}
          >
            <Stack direction="row" spacing={1.5} alignItems="center">
              <TerminalRoundedIcon color="primary" />
              <Typography variant="h6">{definition.label}</Typography>
              <Chip label="Read only" size="small" variant="outlined" />
            </Stack>
            <Typography
              variant="body2"
              color="text.secondary"
              sx={{ mt: 1, mb: 3 }}
            >
              {definition.detail}
            </Typography>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
              {requiresNode && (
                <TextField
                  label="Node ID"
                  value={nodeId}
                  onChange={(event) => {
                    changeNode(event.target.value);
                  }}
                  type="number"
                  fullWidth
                  error={nodeId !== "" && !valid}
                  helperText={
                    nodeId && !valid
                      ? "Enter a positive whole number."
                      : (selectedLabel ??
                        "Persisted symbol ID from Explorer or Search.")
                  }
                  slotProps={{
                    input: {
                      startAdornment: (
                        <InputAdornment position="start">#</InputAdornment>
                      ),
                    },
                    htmlInput: { min: 1, step: 1 },
                  }}
                  disabled={run.isPending}
                />
              )}
              {tool === "trace" && (
                <TextField
                  select
                  label="Direction"
                  value={direction}
                  onChange={(event) => {
                    setDirection(event.target.value as "caller" | "callee");
                    run.reset();
                  }}
                  sx={{ minWidth: 170 }}
                  disabled={run.isPending}
                >
                  <MenuItem value="callee">Callees · outgoing</MenuItem>
                  <MenuItem value="caller">Callers · incoming</MenuItem>
                </TextField>
              )}
              {(tool === "trace" || tool === "impact") && (
                <TextField
                  select
                  label="Depth"
                  value={depth}
                  onChange={(event) => {
                    setDepth(Number(event.target.value));
                    run.reset();
                  }}
                  sx={{ minWidth: 100 }}
                  disabled={run.isPending}
                >
                  {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((value) => (
                    <MenuItem key={value} value={value}>
                      {value}
                    </MenuItem>
                  ))}
                </TextField>
              )}
              {tool === "context" && (
                <TextField
                  select
                  label="Related limit"
                  value={maxRelated}
                  onChange={(event) => {
                    setMaxRelated(Number(event.target.value));
                    run.reset();
                  }}
                  sx={{ minWidth: 130 }}
                  disabled={run.isPending}
                >
                  {[5, 10, 20, 50].map((value) => (
                    <MenuItem key={value} value={value}>
                      {value}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            </Stack>
            <Stack direction="row" justifyContent="flex-end" sx={{ mt: 2 }}>
              <Button
                type="submit"
                variant="contained"
                startIcon={<PlayArrowRoundedIcon />}
                disabled={!valid || run.isPending}
                data-testid="run-tool"
              >
                {run.isPending
                  ? "Running…"
                  : "Run " + definition.label.toLowerCase()}
              </Button>
            </Stack>
          </Paper>
          {matchesCurrentParameters && run.error && (
            <ErrorState error={run.error} retry={submit} />
          )}
          <ToolResultView
            result={matchesCurrentParameters ? run.data : undefined}
            loading={matchesCurrentParameters && run.isPending}
            tool={tool}
            onInspect={(id) => {
              changeNode(String(id));
            }}
          />
        </Stack>
      </Box>
    </Stack>
  );
}
