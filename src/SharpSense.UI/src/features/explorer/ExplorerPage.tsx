import { lazy, Suspense, useDeferredValue, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  FormControlLabel,
  IconButton,
  InputAdornment,
  Paper,
  Stack,
  Switch,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Tooltip,
  Typography,
  useMediaQuery,
} from "@mui/material";
import { useColorScheme, type Theme } from "@mui/material/styles";
import FolderOutlinedIcon from "@mui/icons-material/FolderOutlined";
import HubOutlinedIcon from "@mui/icons-material/HubOutlined";
import CenterFocusStrongRoundedIcon from "@mui/icons-material/CenterFocusStrongRounded";
import SearchRoundedIcon from "@mui/icons-material/SearchRounded";
import ViewListRoundedIcon from "@mui/icons-material/ViewListRounded";
import BubbleChartOutlinedIcon from "@mui/icons-material/BubbleChartOutlined";
import ClearRoundedIcon from "@mui/icons-material/ClearRounded";
import type { UseQueryResult } from "@tanstack/react-query";
import type { ExplorerSearch } from "../../app/searchState";
import type {
  GraphNodeSummary,
  ToolSelection,
  WorkspaceOverview,
} from "../../shared/api/models";
import { EmptyState, ErrorState, LoadingRows } from "../../shared/ui/States";
import {
  normalizePaths,
  useScopedGraph,
  useWorkspaceTree,
} from "./useExplorer";
import { WorkspaceTree } from "./WorkspaceTree";
import { NodeInspector } from "./NodeInspector";
import { NodeList } from "./NodeList";
import { GraphTypeFilter } from "./GraphTypeFilter";
import { GraphLoadingStatus } from "./GraphLoadingStatus";
import { useGraphProjection } from "./useGraphProjection";
import { ancestorPaths } from "./workspacePaths";
import { graphPalettes } from "./graphPalette";

const GraphCanvas = lazy(() => import("./GraphCanvas"));

export default function ExplorerPage({
  state,
  onStateChange,
  overview,
  onOpenTool,
}: {
  state: ExplorerSearch;
  onStateChange: (patch: Partial<ExplorerSearch>, replace?: boolean) => void;
  overview: UseQueryResult<WorkspaceOverview, Error>;
  onOpenTool: (selection: ToolSelection) => void;
}) {
  const { mode: colorMode, systemMode } = useColorScheme();
  const palette =
    graphPalettes[(colorMode === "system" ? systemMode : colorMode) ?? "dark"];
  const narrow = useMediaQuery((theme: Theme) => theme.breakpoints.down("lg"));
  const [scopesOpen, setScopesOpen] = useState(false);
  const [expanded, setExpanded] = useState(
    () => new Set(["/", ...state.scopes.flatMap(ancestorPaths)]),
  );
  const selectedPaths = state.scopes;
  const showEdges = state.edges;
  const search = state.filter;
  const mode = state.mode;
  const setSelected = (node: GraphNodeSummary | null) =>
    onStateChange({ selected: node?.id });
  const setShowEdges = (value: boolean) => onStateChange({ edges: value });
  const setSearch = (value: string) => onStateChange({ filter: value }, true);
  const setMode = (value: "graph" | "list") => onStateChange({ mode: value });
  const setSelectedPaths = (
    value: string[] | ((paths: string[]) => string[]),
  ) =>
    onStateChange({
      scopes: typeof value === "function" ? value(selectedPaths) : value,
      selected: undefined,
    });
  const [fitToken, setFitToken] = useState(0);
  const tree = useWorkspaceTree(expanded, state.scopes);
  const directoryIds = selectedPaths
    .map((path) => tree.nodesByPath.get(path))
    .filter((node) => node?.isSelectable)
    .map((node) => node!.id);
  const graph = useScopedGraph(directoryIds, showEdges);
  const deferredSearch = useDeferredValue(search);
  const projected = useGraphProjection(graph.scope, {
    nodePages: graph.nodePages,
    edgePages: graph.edgePages,
    types: state.types,
    search: deferredSearch,
  });
  const visibleGraph = projected.data;
  const filtered = visibleGraph.nodes;
  const typeCounts = projected.typeCounts;
  const currentSelection = state.selected
    ? projected.getNode(state.selected)
    : null;
  const toggleExpanded = (path: string) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(path)) next.delete(path);
      else next.add(path);
      return next;
    });
  const toggleSelected = (path: string) =>
    setSelectedPaths((current) =>
      current.includes(path)
        ? current.filter((item) => item !== path)
        : normalizePaths([...current, path]),
    );
  const types = [...typeCounts.keys()];
  const exploreProject = (node: GraphNodeSummary) => {
    if (!node.relativePath) return;
    const path = node.relativePath.replaceAll("\\", "/");
    const directory = path.slice(0, path.lastIndexOf("/"));
    onStateChange({
      scopes: [path.includes("/") && directory ? directory : "/"],
      selected: undefined,
      filter: "",
    });
  };
  const workspaceTree = (
    <WorkspaceTree
      tree={tree}
      selectedPaths={selectedPaths}
      onToggleExpanded={toggleExpanded}
      onToggleSelected={toggleSelected}
      onClose={narrow ? () => setScopesOpen(false) : undefined}
    />
  );
  const inspector = (
    <NodeInspector
      key={state.selected ?? "empty"}
      nodeId={state.selected}
      node={currentSelection}
      onSelect={setSelected}
      onOpenTool={onOpenTool}
      onExploreProject={exploreProject}
      onClose={narrow ? () => setSelected(null) : undefined}
    />
  );
  return (
    <Stack spacing={2}>
      {overview.error && (
        <ErrorState
          error={overview.error}
          retry={() => {
            void overview.refetch();
          }}
        />
      )}
      {overview.data && !overview.data.isIndexed && (
        <Alert severity="info">
          This workspace has no index yet. Choose Analyze or Analyze &amp; watch
          above to build its graph.
        </Alert>
      )}
      {narrow && (
        <Button
          variant="outlined"
          startIcon={<FolderOutlinedIcon />}
          onClick={() => setScopesOpen(true)}
          data-testid="workspace-scopes-trigger"
          sx={{ alignSelf: "flex-start" }}
        >
          {selectedPaths.length
            ? "Scopes (" + selectedPaths.length + ")"
            : "Choose scopes"}
        </Button>
      )}
      <Box
        sx={{
          display: "grid",
          gridTemplateColumns: { xs: "1fr", lg: "230px minmax(0, 1fr) 300px" },
          gap: 2,
          alignItems: "stretch",
          minHeight: { lg: 580 },
        }}
      >
        {narrow ? (
          <Drawer
            open={scopesOpen}
            onClose={() => setScopesOpen(false)}
            slotProps={{
              paper: {
                role: "dialog",
                "aria-label": "Workspace scopes",
                sx: { width: "min(90vw, 360px)" },
              },
            }}
          >
            {workspaceTree}
          </Drawer>
        ) : (
          <Paper
            variant="outlined"
            sx={{
              overflow: "hidden",
              height: { xs: 270, lg: "calc(100dvh - 250px)" },
              minHeight: { lg: 580 },
            }}
          >
            {workspaceTree}
          </Paper>
        )}
        <Paper
          variant="outlined"
          sx={{
            display: "flex",
            flexDirection: "column",
            overflow: "hidden",
            minWidth: 0,
            minHeight: 540,
            height: { lg: "calc(100dvh - 250px)" },
          }}
        >
          <Stack
            direction="row"
            sx={{
              alignItems: "center",
              justifyContent: "space-between",
              px: 2,
              py: 1.5,
            }}
            spacing={1}
          >
            <Stack direction="row" spacing={1} sx={{ alignItems: "center" }}>
              <HubOutlinedIcon color="primary" fontSize="small" />
              <Typography variant="subtitle2">Dependency graph</Typography>
              {graph.fetching && <CircularProgress size={14} />}
            </Stack>
            <ToggleButtonGroup
              size="small"
              exclusive
              value={mode}
              onChange={(_, value: "graph" | "list" | null) =>
                value && setMode(value)
              }
              aria-label="Graph view"
            >
              <ToggleButton value="graph" aria-label="3D graph view">
                <BubbleChartOutlinedIcon fontSize="small" />
              </ToggleButton>
              <ToggleButton
                value="list"
                aria-label="Node list view"
                data-testid="node-list-toggle"
              >
                <ViewListRoundedIcon fontSize="small" />
              </ToggleButton>
            </ToggleButtonGroup>
          </Stack>
          <Divider />
          <Stack
            direction={{ xs: "column", sm: "row" }}
            sx={{ alignItems: { sm: "center" }, p: 1.5 }}
            spacing={1}
          >
            <TextField
              fullWidth
              size="small"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Filter symbols, types, or paths"
              slotProps={{
                input: {
                  startAdornment: (
                    <InputAdornment position="start">
                      <SearchRoundedIcon fontSize="small" />
                    </InputAdornment>
                  ),
                },
                htmlInput: { "aria-label": "Filter graph nodes" },
              }}
            />
            <Stack
              direction="row"
              spacing={1}
              useFlexGap
              sx={{ alignItems: "center", flexShrink: 0 }}
            >
              <GraphTypeFilter
                counts={typeCounts}
                selected={state.types}
                onChange={(value) => onStateChange({ types: value }, true)}
              />
              <FormControlLabel
                sx={{ m: 0 }}
                control={
                  <Switch
                    size="small"
                    checked={showEdges}
                    data-testid="toggle-edges-checkbox"
                    onChange={(event) => setShowEdges(event.target.checked)}
                  />
                }
                label={<Typography variant="caption">Edges</Typography>}
              />
              <Tooltip title="Fit graph to view">
                <span>
                  <IconButton
                    size="small"
                    aria-label="Fit graph to view"
                    disabled={mode !== "graph" || graph.loadedNodes === 0}
                    onClick={() => setFitToken((current) => current + 1)}
                  >
                    <CenterFocusStrongRoundedIcon fontSize="small" />
                  </IconButton>
                </span>
              </Tooltip>
            </Stack>
          </Stack>
          {selectedPaths.length > 0 && (
            <Stack
              direction="row"
              spacing={0.5}
              sx={{ px: 1.5, pb: 1.5, flexWrap: "wrap", gap: 0.5 }}
            >
              {selectedPaths.map((path) => (
                <Chip
                  key={path}
                  size="small"
                  variant="outlined"
                  label={path === "/" ? "Entire workspace" : path}
                  onDelete={() => toggleSelected(path)}
                />
              ))}
              <Tooltip title="Clear selected scopes">
                <IconButton
                  size="small"
                  aria-label="Clear selected scopes"
                  onClick={() => setSelectedPaths([])}
                >
                  <ClearRoundedIcon sx={{ fontSize: 16 }} />
                </IconButton>
              </Tooltip>
            </Stack>
          )}
          {directoryIds.length > 0 && (
            <GraphLoadingStatus graph={graph} showEdges={showEdges} />
          )}
          <Divider />
          <Box sx={{ flex: 1, minHeight: 340, overflow: "hidden" }}>
            {graph.error && graph.loadedNodes === 0 ? (
              <Box sx={{ p: 2 }}>
                <ErrorState error={graph.error} retry={graph.retry} />
              </Box>
            ) : graph.pending ? (
              <LoadingRows count={7} />
            ) : directoryIds.length === 0 ? (
              <Box data-testid="graph-empty-state" sx={{ height: "100%" }}>
                <EmptyState
                  icon={<HubOutlinedIcon />}
                  title="See how your code connects"
                  description="Select a folder in the workspace to explore its symbols and dependencies. Your graph starts with the scope you choose."
                />
              </Box>
            ) : graph.loadedNodes === 0 ? (
              <EmptyState
                title="No symbols in this scope"
                description="Choose another folder or refresh the workspace after indexing."
              />
            ) : filtered.length === 0 ? (
              <Stack
                spacing={1}
                sx={{
                  alignItems: "center",
                  justifyContent: "center",
                  height: "100%",
                  p: 2,
                }}
              >
                <EmptyState
                  compact
                  title="No matching nodes"
                  description="Change the visible types or clear the filter to explore the loaded graph."
                />
                <Button
                  onClick={() =>
                    onStateChange({ types: ["*"], filter: "" }, true)
                  }
                >
                  Show all types
                </Button>
              </Stack>
            ) : mode === "graph" ? (
              <Suspense fallback={<LoadingRows />}>
                <GraphCanvas
                  palette={palette}
                  data={visibleGraph}
                  stream={projected.stream}
                  selected={currentSelection}
                  search={deferredSearch}
                  fitToken={fitToken}
                  onSelect={setSelected}
                />
              </Suspense>
            ) : (
              <NodeList
                key={JSON.stringify([selectedPaths, search, state.types])}
                nodes={filtered}
                selectedId={currentSelection?.id}
                onSelect={setSelected}
              />
            )}
          </Box>
          <Divider />
          <Stack
            direction="row"
            sx={{
              flexWrap: "wrap",
              alignItems: "center",
              justifyContent: "space-between",
              px: 2,
              py: 1.2,
              gap: 1,
            }}
          >
            <Typography
              variant="caption"
              color="text.secondary"
              data-testid="graph-counts"
            >
              {filtered.length.toLocaleString()} visible /{" "}
              {graph.loadedNodes.toLocaleString()} loaded nodes ·{" "}
              {visibleGraph.edges.length.toLocaleString()} visible edges
            </Typography>
            <Stack direction="row" spacing={1.5} sx={{ flexWrap: "wrap" }}>
              {types.slice(0, 5).map((type) => (
                <Stack
                  key={type}
                  direction="row"
                  spacing={0.5}
                  sx={{ alignItems: "center" }}
                >
                  <Box
                    sx={{
                      width: 6,
                      height: 6,
                      borderRadius: "50%",
                      bgcolor: palette.nodes[type] ?? palette.fallback,
                    }}
                  />
                  <Typography variant="caption" color="text.secondary">
                    {type}
                  </Typography>
                </Stack>
              ))}
            </Stack>
          </Stack>
        </Paper>
        {narrow ? (
          <Drawer
            anchor="bottom"
            open={Boolean(state.selected)}
            onClose={() => setSelected(null)}
            slotProps={{
              paper: {
                role: "dialog",
                "aria-label": "Symbol inspector",
                sx: { maxHeight: "85dvh", borderRadius: "12px 12px 0 0" },
              },
            }}
          >
            {inspector}
          </Drawer>
        ) : (
          <Paper
            variant="outlined"
            sx={{
              overflow: "auto",
              height: { lg: "calc(100dvh - 250px)" },
              minHeight: { lg: 580 },
            }}
          >
            {inspector}
          </Paper>
        )}
      </Box>
    </Stack>
  );
}
