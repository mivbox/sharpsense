import { useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Box,
  Button,
  Chip,
  Divider,
  Drawer,
  InputAdornment,
  List,
  ListItemButton,
  MenuItem,
  Paper,
  Stack,
  TextField,
  Typography,
  useMediaQuery,
} from "@mui/material";
import SearchRoundedIcon from "@mui/icons-material/SearchRounded";
import type { Theme } from "@mui/material/styles";
import { SearchResultDetails } from "./SearchResultDetails";
import DataObjectRoundedIcon from "@mui/icons-material/DataObjectRounded";
import { useWorkspaceApi } from "../../shared/workspace/context";
import type { ToolSelection } from "../../shared/api/models";
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
    queryKey: ["search", state.q, state.limit],
    enabled: Boolean(state.q.trim()),
    queryFn: async ({ signal }) => {
      const started = performance.now();
      return {
        hits: await searchWorkspace(state.q, state.limit, signal),
        query: state.q,
        elapsed: performance.now() - started,
      };
    },
  });
  const selected =
    search.data?.hits.find((hit) => hit.nodeId === selectedNodeId) ?? null;
  const run = (value = query) => {
    const text = value.trim();
    if (!text) return;
    setQuery(text);
    if (text === state.q && limit === state.limit) void search.refetch();
    else onStateChange({ q: text, limit });
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
          <TextField
            select
            label="Results"
            value={limit}
            onChange={(event) => setLimit(Number(event.target.value))}
            sx={{ minWidth: 100 }}
          >
            {[10, 20, 50].map((value) => (
              <MenuItem value={value} key={value}>
                {value}
              </MenuItem>
            ))}
          </TextField>
          <Button
            type="submit"
            variant="contained"
            disabled={!query.trim() || search.isLoading}
            startIcon={<SearchRoundedIcon />}
            sx={{ minWidth: 120 }}
            data-testid="run-search"
          >
            {search.isLoading ? "Searching…" : "Search"}
          </Button>
        </Stack>
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
