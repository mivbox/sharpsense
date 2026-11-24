import { useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Box,
  Button,
  Chip,
  Collapse,
  Divider,
  Drawer,
  FormControlLabel,
  InputAdornment,
  List,
  ListItemButton,
  MenuItem,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
  useMediaQuery,
} from "@mui/material";
import SearchRoundedIcon from "@mui/icons-material/SearchRounded";
import TuneRoundedIcon from "@mui/icons-material/TuneRounded";
import type { Theme } from "@mui/material/styles";
import { SearchResultDetails } from "./SearchResultDetails";
import DataObjectRoundedIcon from "@mui/icons-material/DataObjectRounded";
import { useWorkspaceApi } from "../../shared/workspace/context";
import {
  searchNodeTypes,
  type SearchNodeType,
  type ToolSelection,
} from "../../shared/api/models";
import type { WorkspaceSearch } from "../../app/searchState";
import { EmptyState, ErrorState, LoadingRows } from "../../shared/ui/States";

export default function SearchPage({
  state,
  onStateChange,
  onOpenTool,
}: {
  state: WorkspaceSearch;
  onStateChange: (next: WorkspaceSearch) => void;
  onOpenTool: (selection: ToolSelection) => void;
}) {
  const { searchWorkspace } = useWorkspaceApi();
  const narrow = useMediaQuery((theme: Theme) => theme.breakpoints.down("lg"));
  const [query, setQuery] = useState(state.q);
  const [limit, setLimit] = useState(state.limit);
  const [project, setProject] = useState(state.project);
  const [nodeTypes, setNodeTypes] = useState<SearchNodeType[]>(state.nodeTypes);
  const [includeMemories, setIncludeMemories] = useState(state.includeMemories);
  const activeFilterCount =
    (project.trim() ? 1 : 0) +
    (includeMemories ? 1 : 0) +
    (nodeTypes.length > 0 ? 1 : 0);
  const [filtersOpen, setFiltersOpen] = useState(activeFilterCount > 0);
  const [selectedNodeId, setSelectedNodeId] = useState<number | null>(null);
  const queryClient = useQueryClient();
  const recent = [
    ...new Set(
      queryClient
        .getQueriesData<{ query: string }>({ queryKey: ["search"] })
        .map(([, value]) => value?.query)
        .filter(
          (value): value is string => Boolean(value) && value !== state.q,
        ),
    ),
  ]
    .reverse()
    .slice(0, 4);
  const input = useRef<HTMLInputElement | null>(null);
  const search = useQuery({
    queryKey: [
      "search",
      state.q,
      state.limit,
      state.project,
      state.nodeTypes,
      state.includeMemories,
    ],
    enabled: Boolean(state.q.trim()),
    queryFn: async ({ signal }) => {
      const started = performance.now();
      return {
        hits: await searchWorkspace(state.q, state.limit, signal, {
          project: state.project || undefined,
          nodeTypes: state.nodeTypes.length > 0 ? state.nodeTypes : undefined,
          includeMemories: state.includeMemories,
        }),
        query: state.q,
        elapsed: performance.now() - started,
      };
    },
  });
  const selected =
    search.data?.hits.find((hit) => hit.nodeId === selectedNodeId) ?? null;
  const toggleNodeType = (nodeType: SearchNodeType) => {
    setNodeTypes((previous) => {
      const next = previous.includes(nodeType)
        ? previous.filter((item) => item !== nodeType)
        : [...previous, nodeType];
      return searchNodeTypes.filter((item) => next.includes(item));
    });
  };
  const run = (value = query) => {
    const text = value.trim();
    if (!text) return;
    const trimmedProject = project.trim();
    setQuery(text);
    const sameFilters =
      text === state.q &&
      limit === state.limit &&
      trimmedProject === state.project &&
      includeMemories === state.includeMemories &&
      JSON.stringify(nodeTypes) === JSON.stringify(state.nodeTypes);
    if (sameFilters) void search.refetch();
    else
      onStateChange({
        q: text,
        limit,
        project: trimmedProject,
        nodeTypes,
        includeMemories,
      });
  };
  return (
    <Stack spacing={2.5}>
      <Paper
        variant="outlined"
        component="form"
        onSubmit={(event) => {
          event.preventDefault();
          run();
        }}
        sx={{ p: 2.5 }}
      >
        <Stack direction={{ xs: "column", sm: "row" }} spacing={1.5}>
          <TextField
            autoFocus
            inputRef={input}
            fullWidth
            label="Search code and documentation"
            placeholder="Symbol, namespace, file, or concept"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            slotProps={{
              input: {
                startAdornment: (
                  <InputAdornment position="start">
                    <SearchRoundedIcon />
                  </InputAdornment>
                ),
              },
            }}
          />
          <Stack direction="row" spacing={1.5} sx={{ flexShrink: 0 }}>
            <TextField
              select
              label="Results"
              value={limit}
              onChange={(event) => setLimit(Number(event.target.value))}
              sx={{ minWidth: 96 }}
            >
              {[10, 20, 50].map((value) => (
                <MenuItem value={value} key={value}>
                  {value}
                </MenuItem>
              ))}
            </TextField>
            <Button
              type="button"
              variant={
                filtersOpen || activeFilterCount > 0 ? "outlined" : "text"
              }
              color={activeFilterCount > 0 ? "primary" : "inherit"}
              startIcon={<TuneRoundedIcon />}
              onClick={() => setFiltersOpen((open) => !open)}
              aria-expanded={filtersOpen}
              data-testid="toggle-search-filters"
              sx={{ flexShrink: 0 }}
            >
              {activeFilterCount > 0
                ? `Filters (${activeFilterCount})`
                : "Filters"}
            </Button>
            <Button
              type="submit"
              variant="contained"
              disabled={!query.trim() || search.isLoading}
              startIcon={<SearchRoundedIcon />}
              sx={{ minWidth: 112, flex: { xs: 1, sm: "0 0 auto" } }}
              data-testid="run-search"
            >
              {search.isLoading ? "Searching…" : "Search"}
            </Button>
          </Stack>
        </Stack>
        <Collapse in={filtersOpen} unmountOnExit>
          <Stack
            direction={{ xs: "column", md: "row" }}
            spacing={1.5}
            sx={{ mt: 2, alignItems: { md: "center" } }}
          >
            <TextField
              size="small"
              label="Project filter"
              placeholder="Project name or ID"
              value={project}
              onChange={(event) => setProject(event.target.value)}
              sx={{ minWidth: { sm: 220 } }}
            />
            <FormControlLabel
              control={
                <Switch
                  size="small"
                  checked={includeMemories}
                  onChange={(event) => setIncludeMemories(event.target.checked)}
                />
              }
              label="Include memories"
              sx={{ m: 0 }}
            />
            <Stack
              direction="row"
              spacing={0.75}
              sx={{ flexWrap: "wrap", alignItems: "center", gap: 0.75 }}
            >
              {searchNodeTypes.map((type) => {
                const active = nodeTypes.includes(type);
                return (
                  <Chip
                    key={type}
                    label={type}
                    size="small"
                    color={active ? "primary" : "default"}
                    variant={active ? "filled" : "outlined"}
                    onClick={() => toggleNodeType(type)}
                    disabled={search.isLoading}
                  />
                );
              })}
            </Stack>
          </Stack>
        </Collapse>
        {recent.length > 0 && (
          <Stack
            direction="row"
            spacing={1}
            sx={{ flexWrap: "wrap", alignItems: "center", mt: 2 }}
          >
            <Typography variant="caption" color="text.secondary">
              Recent
            </Typography>
            {recent.map((item) => (
              <Chip
                key={item}
                label={item}
                size="small"
                variant="outlined"
                onClick={() => run(item)}
                disabled={search.isLoading}
              />
            ))}
          </Stack>
        )}
      </Paper>
      {search.error && <ErrorState error={search.error} retry={() => run()} />}
      <Box
        sx={{
          display: "grid",
          gridTemplateColumns: {
            xs: "1fr",
            lg: selected ? "minmax(0, 1fr) 350px" : "1fr",
          },
          gap: 2,
        }}
      >
        <Paper variant="outlined" sx={{ minHeight: 460, overflow: "hidden" }}>
          <Stack
            direction="row"
            sx={{
              alignItems: "center",
              justifyContent: "space-between",
              p: 2.5,
            }}
          >
            <Typography variant="subtitle1">Search results</Typography>
            {search.data && !search.isLoading && (
              <Typography variant="caption" color="text.secondary">
                {search.data.hits.length} matches ·{" "}
                {Math.round(search.data.elapsed).toLocaleString()} ms
              </Typography>
            )}
          </Stack>
          <Divider />
          {search.isLoading ? (
            <LoadingRows count={7} />
          ) : search.data?.hits.length === 0 ? (
            <EmptyState
              icon={<SearchRoundedIcon />}
              title="No results for this search"
              description="Try a broader symbol name or a different keyword. Search covers the current workspace index."
            />
          ) : search.data ? (
            <List disablePadding aria-label="Search results">
              {search.data.hits.map((hit) => (
                <Box key={hit.nodeId}>
                  <ListItemButton
                    selected={selected?.nodeId === hit.nodeId}
                    onClick={() => setSelectedNodeId(hit.nodeId)}
                    data-search-node-id={hit.nodeId}
                    sx={{ py: 2, px: 2.5, alignItems: "flex-start" }}
                  >
                    <DataObjectRoundedIcon
                      color="primary"
                      sx={{
                        mt: 0.5,
                        mr: 2,
                        display: { xs: "none", sm: "block" },
                      }}
                    />
                    <Box sx={{ minWidth: 0, flex: 1 }}>
                      <Stack
                        direction="row"
                        spacing={1}
                        sx={{
                          justifyContent: "space-between",
                          alignItems: "flex-start",
                        }}
                      >
                        <Typography
                          variant="subtitle2"
                          sx={{ overflowWrap: "anywhere" }}
                        >
                          {hit.label}
                        </Typography>
                        <Chip
                          size="small"
                          label={hit.kind}
                          variant="outlined"
                          sx={{ flexShrink: 0 }}
                        />
                      </Stack>
                      <Typography
                        variant="caption"
                        color="text.secondary"
                        sx={{
                          display: "block",
                          mt: 0.5,
                          overflowWrap: "anywhere",
                        }}
                      >
                        {hit.path}
                        {hit.startLine ? ":" + hit.startLine : ""} · #
                        {hit.nodeId}
                      </Typography>
                      {hit.summary && (
                        <Typography
                          variant="body2"
                          color="text.secondary"
                          sx={{
                            mt: 1,
                            display: "-webkit-box",
                            WebkitLineClamp: 2,
                            WebkitBoxOrient: "vertical",
                            overflow: "hidden",
                            overflowWrap: "anywhere",
                          }}
                        >
                          {hit.summary}
                        </Typography>
                      )}
                    </Box>
                  </ListItemButton>
                  <Divider />
                </Box>
              ))}
            </List>
          ) : (
            <EmptyState
              icon={<SearchRoundedIcon />}
              title="Find your starting point"
              description="Search for a symbol, concept, or documentation. Open a result to explore its context and dependencies."
            />
          )}
        </Paper>
        {selected &&
          (narrow ? (
            <Drawer
              anchor="bottom"
              open
              onClose={() => setSelectedNodeId(null)}
              slotProps={{
                paper: {
                  role: "dialog",
                  "aria-label": "Selected search result",
                  sx: {
                    maxHeight: "85dvh",
                    borderRadius: "12px 12px 0 0",
                    p: 2.5,
                  },
                },
              }}
            >
              <SearchResultDetails
                selected={selected}
                onOpenTool={onOpenTool}
                onClose={() => setSelectedNodeId(null)}
              />
            </Drawer>
          ) : (
            <Paper variant="outlined" sx={{ p: 2.5, alignSelf: "start" }}>
              <SearchResultDetails
                selected={selected}
                onOpenTool={onOpenTool}
              />
            </Paper>
          ))}
      </Box>
    </Stack>
  );
}
